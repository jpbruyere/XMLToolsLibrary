using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using XMLTools;

namespace svgparsergen
{
	class Program
	{
		class AttributeDefInfo { //: IEquatable<AttributeDefInfo> {
			public string attGroup;
			public AttributeDef attDef;
			public string name => attDef.Name.ToString();
			public string define => attGroup == null ? name.ToUpper().Replace('-','_').Replace(':','_') : $"{attGroup.ToUpper().Replace('.','_').Replace('-','_')}_{name.ToUpper().Replace('-','_').Replace(':','_')}";
			public AttributeDefInfo (AttributeDef attDef, string attGroup = null) {
				this.attDef = attDef;
				this.attGroup = attGroup;
			}
			/*public static bool operator ==(AttributeDefInfo left, AttributeDefInfo right) => left is AttributeDefInfo ? left.Equals(right) : false;
			public static bool operator !=(AttributeDefInfo left, AttributeDefInfo right) => left is AttributeDefInfo ? !left.Equals(right) : false;
			public bool Equals(AttributeDefInfo other)
				=> other is AttributeDefInfo ? other.name == this.name : false;

			public override bool Equals(object obj) => obj is AttributeDefInfo adi && adi.Equals (this);

			public override int GetHashCode() => name.GetHashCode();

			public override string ToString() => name;*/
		}
		static void processAttributeDef (List<AttributeDefInfo> attributes, AttributeDef adef, string attributeGroup = null) {
			if (adef == null)
				System.Diagnostics.Debugger.Break();
			attributes.Add (new AttributeDefInfo(adef, attributeGroup));
		}
		static void extractAttlistDecl (List<AttributeDefInfo> attributes, AttlistDecl ad, string attributeGroup = null) {
			foreach (XMLToken item in ad.attributeDef) {
				if (item is PEReference pe) {
					AttlistDecl adecl = pe.Extract<AttlistDecl>();
					if (adecl.attributeDef.Count == 1) {
						if (adecl.attributeDef[0] is PEReference pe2)
							processAttributeDef (attributes, pe2.Extract<AttlistDecl>().attributeDef[0] as AttributeDef, attributeGroup);
						else
							processAttributeDef (attributes, adecl.attributeDef[0] as AttributeDef, attributeGroup);
					} else
						extractAttlistDecl (attributes, adecl, pe.CompiledName);
				} else
					processAttributeDef (attributes, item as AttributeDef, attributeGroup);
			}
		}
		static void processParticle (Particle p, List<string> childs) {
			foreach (XMLToken c in p.Children) {
				if (c is PEReference pe) {
					var tmp = pe.CompiledValue;
					if (tmp is Particle pa)
						processParticle (pa, childs);
					else if (tmp is ParticleBase pb) {
						string name = normalize (pb.Name.ToString());
						if (!childs.Contains(name))
							childs.Add (name);
					} else {
						ContentSpec ocs = c.Extract<ContentSpec>();
						if (ocs != null)
							contentSpecProcessing (ocs, childs);
					}
				} else if (c is Particle op)
					processParticle (op, childs);
				else
					System.Diagnostics.Debugger.Break();
			}
		}
		static void contentSpecProcessing (ContentSpec cs, List<string> childs) {
			if (cs.contentType == ContentTypes.Empty)
				return;
			if (cs is Particle p) {
				processParticle (p, childs);
			} else {
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine ($"\t\t{cs.contentType}");
			}
		}
		static void process_attributes (int level, IndentedTextWriter tw, IEnumerable<IGrouping<char, AttributeDefInfo>> elts, StringBuilder chain) {
			const string defaultcase = @"LOG(""Unexpected attribute: %s->%s\n"", svg->att, svg->att);";
			if (elts.Count() > 1) {
				bool chainTest = false;
				if (chain.Length > 0) {
					if (chain.Length == 1)
						tw.WriteLine ($"if (tolower(svg->att[{level-chain.Length}]) == \'{chain[0]}\') {{//up");
					else
						tw.WriteLine ($"if (!strncasecmp (&svg->att[{level-chain.Length}],\"{chain.ToString()}\",{chain.Length})) {{//up");
					tw.Indent++;
					chain.Clear();
					chainTest = true;
				}
				tw.WriteLine ($"switch(tolower(svg->att[{level}])) {{");
				foreach (IGrouping<char, AttributeDefInfo> elt in elts) {
					tw.WriteLine ($"case '{char.ToLower(elt.Key)}':");
					if (elt.Count() == 1) {
						tw.Indent++;
						tw.WriteLine ($"PROCESS_{elt.First().define}");
						tw.WriteLine ($"break;");
						tw.Indent--;
						continue;
					}
					AttributeDefInfo ed = elt.FirstOrDefault (e=>e.name.Length == level + 1);
					if (ed != null) {
						tw.Indent++;
						tw.WriteLine ($"if (nameLenght == {level + 1})");
						tw.Indent++;
						tw.WriteLine ($"PROCESS_{ed.define}");
						tw.Indent--;
						tw.WriteLine (@"else {");
						tw.Indent++;
						process_attributes (level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e.name[level+1]), chain);
						tw.Indent--;
						tw.WriteLine (@"}");
					} else {
						tw.Indent++;
						process_attributes (level+1, tw, elt.GroupBy (e=>e.name[level+1]), chain);
					}
					tw.WriteLine ($"break;");
					tw.Indent--;
				}
				tw.WriteLine (@"default:");
				tw.Indent++;
				tw.WriteLine (defaultcase);
				tw.WriteLine ($"break;");
				tw.Indent--;
				tw.WriteLine (@"}");
				if (chainTest) {
					tw.Indent--;
					tw.WriteLine (@"} else");
					tw.Indent++;
					tw.WriteLine (defaultcase);
					tw.Indent--;
				}
			} else {
				IGrouping<char, AttributeDefInfo> elt = elts.First();
				if (elt.Count() == 1) {
					AttributeDefInfo c = elt.First();
					tw.WriteLine ($"if (!strcasecmp (&svg->att[{level}],\"{c.name.Substring(level).ToLower()}\"))");
					tw.Indent++;
					tw.WriteLine ($"PROCESS_{c.define}");
					tw.Indent--;
					tw.WriteLine ($"else");
					tw.Indent++;
					tw.WriteLine (defaultcase);
					tw.Indent--;
					return;
				}
				chain.Append (char.ToLower(elt.Key));
				AttributeDefInfo ed = elt.FirstOrDefault (e=>e.name.Length == level + 1);
				if (ed != null) {
					if (chain.Length == 1)
						tw.WriteLine ($"if (tolower(svg->att[{level-chain.Length+1}]) == \'{chain[0]}\') {{//down");
					else
						tw.WriteLine ($"if (!strncasecmp (&svg->att[{level-chain.Length+1}],\"{chain.ToString()}\",{chain.Length})) {{//down");
					tw.Indent++;
					tw.WriteLine ($"if (nameLenght == {level + 1})");
					tw.Indent++;
					tw.WriteLine ($"PROCESS_{ed.define}");
					tw.Indent--;
					chain.Clear();
					tw.WriteLine (@"else {");
					tw.Indent++;
					process_attributes (level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e.name[level+1]), chain);
					tw.Indent--;
					tw.WriteLine (@"}");
					tw.Indent--;
					tw.WriteLine (@"} else");
					tw.Indent++;
					tw.WriteLine (defaultcase);
					tw.Indent--;
				} else
					process_attributes (level+1, tw, elt.GroupBy (e=>e.name[level+1]), chain);
			}
		}

		static void process_elements (int level, IndentedTextWriter tw, IEnumerable<IGrouping<char, string>> elts, StringBuilder chain) {
			const string commonSig = "svg, f, hasStroke, hasFill, stroke, fill";
			if (elts.Count() > 1) {
				bool chainTest = false;
				if (chain.Length > 0) {
					if (chain.Length == 1)
						tw.WriteLine ($"if (tolower(svg->elt[{level-chain.Length}]) == \'{chain[0]}\') {{//up");
					else
						tw.WriteLine ($"if (!strncasecmp (&svg->elt[{level-chain.Length}],\"{chain.ToString()}\",{chain.Length})) {{//up");
					tw.Indent++;
					chain.Clear();
					chainTest = true;
				}
				tw.WriteLine ($"switch(tolower(svg->elt[{level}])) {{");
				foreach (IGrouping<char, string> elt in elts) {
					tw.WriteLine ($"case '{char.ToLower(elt.Key)}':");
					if (elt.Count() == 1) {
						tw.Indent++;
						tw.WriteLine ($"res = read_{elt.First()}_attributes ({commonSig});");
						tw.WriteLine ($"break;");
						tw.Indent--;
						continue;
					}
					string ed = elt.FirstOrDefault (e=>e.Length == level + 1);
					if (ed != null) {
						tw.Indent++;
						tw.WriteLine ($"if (nameLenght == {level + 1})");
						tw.Indent++;
						tw.WriteLine ($"res = read_{ed}_attributes ({commonSig});");
						tw.Indent--;
						tw.WriteLine (@"else {");
						tw.Indent++;
						process_elements (level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e[level+1]), chain);
						tw.Indent--;
						tw.WriteLine (@"}");
					} else {
						tw.Indent++;
						process_elements (level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
					}
					tw.WriteLine ($"break;");
					tw.Indent--;
				}
				tw.WriteLine (@"default:");
				tw.Indent++;
				tw.WriteLine (@"skip_element");
				tw.WriteLine ($"break;");
				tw.Indent--;
				tw.WriteLine (@"}");
				if (chainTest) {
					tw.Indent--;
					tw.WriteLine (@"} else");
					tw.Indent++;
					tw.WriteLine (@"skip_element");
					tw.Indent--;
				}
			} else {
				IGrouping<char, string> elt = elts.First();
				if (elt.Count() == 1) {
					string c = elt.First();
					tw.WriteLine ($"if (!strcasecmp (&svg->elt[{level}],\"{c.Substring(level).ToLower()}\"))");
					tw.Indent++;
					tw.WriteLine ($"res = read_{c}_attributes ({commonSig});");
					tw.Indent--;
					tw.WriteLine ($"else");
					tw.Indent++;
					tw.WriteLine ($"skip_element");
					tw.Indent--;
					return;
				}
				chain.Append (char.ToLower(elt.Key));
				string ed = elt.FirstOrDefault (e=>e.Length == level + 1);
				if (ed != null) {
					if (chain.Length == 1)
						tw.WriteLine ($"if (tolower(svg->elt[{level-chain.Length+1}]) == \'{chain[0]}\') {{//down");
					else
						tw.WriteLine ($"if (!strncasecmp (&svg->elt[{level-chain.Length+1}],\"{chain.ToString()}\",{chain.Length})) {{//down");
					tw.Indent++;
					tw.WriteLine ($"if (nameLenght == {level + 1})");
					tw.Indent++;
					tw.WriteLine ($"res = read_{ed}_attributes ({commonSig});");
					tw.Indent--;
					chain.Clear();
					tw.WriteLine (@"else {");
					tw.Indent++;
					process_elements (level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e[level+1]), chain);
					tw.Indent--;
					tw.WriteLine (@"}");
					tw.Indent--;
					tw.WriteLine (@"} else");
					tw.Indent++;
					tw.WriteLine ($"skip_element");
					tw.Indent--;
				} else
					process_elements (level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
			}
		}
		static string normalize (string str) => str.Replace ('-', '_');
		static string normalize_def (string str) => str.Replace ('-', '_').ToUpper();
		const string source_name = @"parser_gen";
		static void Main(string[] args)
		{
			string test = System.IO.Directory.GetCurrentDirectory();
			XMLDocument dtd = new XMLDocument ("data/svg11-flat.dtd");

			Dictionary<string, List<AttributeDefInfo>> attributes = new Dictionary<string, List<AttributeDefInfo>> ();
			foreach (AttlistDecl ad in dtd.Attributes) {
				List<AttributeDefInfo> attribs = new List<AttributeDefInfo>();
				extractAttlistDecl (attribs, ad, ad.Name.ToString());
				attributes.Add (ad.Name.ToString(), attribs);
			}

			foreach (KeyValuePair<string, List<AttributeDefInfo>> kvp in attributes) {
				Console.WriteLine ($"{kvp.Key}");
				foreach (AttributeDefInfo adi in kvp.Value) {
					Console.WriteLine ($"\t{adi.attGroup}:{adi.attDef.Name}");
				}
			}

			Dictionary<string, List<string>> parenting = new Dictionary<string, List<string>>();

			foreach (ElementDecl e in dtd.Elements) {
				List<string> childs = new List<string>();
				ContentSpec cs = e.contentSpec.Extract<ContentSpec>();
				if (cs.contentType == ContentTypes.Empty)
					continue;
				contentSpecProcessing (cs, childs);
				parenting.Add (e.Name.ToString(), childs);
			}

			const string commonSig = "svg_context* svg, FILE* f, svg_paint_type hasStroke, svg_paint_type hasFill, uint32_t stroke, uint32_t fill";
			const string commonSigCall = "svg, f, hasStroke, hasFill, stroke, fill";

			//string[] skip_funcs = {"rect","cirlce","svg","line","polygon","polyline","g","defs","stop","cirlce","path"};
			using (Stream stream = new FileStream ($"/mnt/devel/tests/svgParser/src/{source_name}.h", FileMode.Create)) {
				using (StreamWriter sw = new StreamWriter(stream)) {
					using (IndentedTextWriter tw = new IndentedTextWriter (sw, "\t")) {
						tw.WriteLine ($"#ifndef {source_name.ToUpper()}_H");
						tw.WriteLine ($"#define {source_name.ToUpper()}_H");
						tw.WriteLine ("#include \"vkvg_svg_internal.h\"\n");

						const string defaultattribdef = @"LOG(""Unprocessed attribute: %s->%s\n"", svg->elt, svg->att);";

						foreach (string define in attributes.Values.SelectMany (al=>al).Select (a=>a.define).Distinct()) {
							tw.WriteLine ($"#ifndef PROCESS_{define}");
							tw.Indent++;
							tw.WriteLine ($"#define PROCESS_{define} {{{defaultattribdef}}}");
							tw.Indent--;
							tw.WriteLine ($"#endif");
						}
						foreach (string elt in attributes.Keys) {
							string elt_norm = normalize_def(elt);
							tw.WriteLine ($"#ifndef HEADING_{elt_norm}");
							tw.Indent++;
							tw.WriteLine ($"#define HEADING_{elt_norm}");
							tw.Indent--;
							tw.WriteLine ($"#endif");
							tw.WriteLine ($"#ifndef ELEMENT_PRE_PROCESS_{elt_norm}");
							tw.Indent++;
							tw.WriteLine ($"#define ELEMENT_PRE_PROCESS_{elt_norm}");
							tw.Indent--;
							tw.WriteLine ($"#endif");
							tw.WriteLine ($"#ifndef ELEMENT_POST_PROCESS_{elt_norm}");
							tw.Indent++;
							tw.WriteLine ($"#define ELEMENT_POST_PROCESS_{elt_norm}");
							tw.Indent--;
							tw.WriteLine ($"#endif");
						}
						foreach (KeyValuePair<string, List<string>> kvp in parenting) {
							if (kvp.Value.Count == 0)
								continue;
							string elt_norm = normalize(kvp.Key);
							tw.WriteLine ($"int read_{elt_norm}_children ({commonSig});");
						}
						foreach (string elt in attributes.Keys) {
							string elt_norm = normalize(elt);
							tw.WriteLine ($"int read_{elt_norm}_attributes ({commonSig});");
						}

						
						tw.WriteLine (@"#endif");
					}
				}
			}
			using (Stream stream = new FileStream ($"/mnt/devel/tests/svgParser/src/{source_name}.c", FileMode.Create)) {
				using (StreamWriter sw = new StreamWriter(stream)) {
					using (IndentedTextWriter tw = new IndentedTextWriter (sw, "\t")) {
						tw.WriteLine ($"#include \"{source_name}.h\"\n");

						foreach (string elt in attributes.Keys) {
							/*if (skip_funcs.Contains(elt))
								continue;*/
							string elt_norm = normalize(elt);
							string elt_def = normalize_def(elt);
							List<AttributeDefInfo> adi = attributes[elt];
							tw.WriteLine ($"int read_{elt_norm}_attributes ({commonSig}) {{");
							tw.Indent++;
							tw.WriteLine ($"HEADING_{elt_def}");
							tw.WriteLine (@"read_attributes_loop_start");
							tw.Indent++;
							tw.WriteLine (@"int nameLenght = strlen (svg->att);");
							process_attributes (0, tw, adi.OrderBy (c=>c.name).GroupBy (e=>e.name[0]), new StringBuilder());
							tw.Indent--;
							tw.WriteLine (@"read_attributes_loop_end");
							tw.WriteLine ($"ELEMENT_PRE_PROCESS_{elt_def}");
							tw.WriteLine (@"read_tag_end");
							if (parenting.ContainsKey (elt) && parenting[elt].Count > 0)
								tw.WriteLine ($"res = read_{elt_norm}_children ({commonSigCall});");
							tw.WriteLine ($"ELEMENT_POST_PROCESS_{elt_def}");
							tw.WriteLine (@"return res;");
							tw.Indent--;
							tw.WriteLine (@"}");
						}
						tw.WriteLine ("\n");

						foreach (KeyValuePair<string, List<string>> kvp in parenting) {
							if (kvp.Value.Count == 0)
								continue;
							tw.WriteLine ($"int read_{normalize (kvp.Key)}_children ({commonSig}) {{");
							tw.Indent++;

							tw.WriteLine (@"int res = 0;");
							tw.WriteLine (@"int nameLenght = strlen (svg->elt);");

							tw.WriteLine (@"while (!feof (f)) {");
							tw.Indent++;

							tw.WriteLine (@"read_element_start");

							process_elements (0,tw, kvp.Value.OrderBy (c=>c).GroupBy (e=>e[0]), new StringBuilder());

							tw.Indent--;
							tw.WriteLine (@"}");
							tw.WriteLine (@"return res;");
							tw.Indent--;
							tw.WriteLine (@"}");
						}
					}
				}
			}
		}
	}
}
