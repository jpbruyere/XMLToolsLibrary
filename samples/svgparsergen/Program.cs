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
		class AttributeDefInfo  : IEquatable<AttributeDefInfo> {
			public string attGroup;
			public AttributeDef attDef;
			public string name => attDef.Name.ToString();
			public string name_normalized => name.ToUpper().Replace('-','_').Replace(':','_');
			public string attGroup_normalized => attGroup.ToUpper().Replace('.','_').Replace('-','_');

			public string define => attGroup == null ? name_normalized : $"{attGroup_normalized}_{name_normalized}";
			public AttributeDefInfo (AttributeDef attDef, string attGroup = null) {
				this.attDef = attDef;
				this.attGroup = attGroup;
			}
			public bool Equals(AttributeDefInfo other)
				=> other is AttributeDefInfo ? other.define == this.define : false;
			public override int GetHashCode() => define.GetHashCode();
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
		static void process_enum_match (IndentedTextWriter tw, AttributeDefInfo adi, string value) {
			tw.WriteLine ($"PROCESS_{adi.define}_{value.ToUpper().Replace('-','_')}");
		}
		static void process_enums (AttributeDefInfo adi, int level, IndentedTextWriter tw, IEnumerable<IGrouping<char, string>> elts, StringBuilder chain) {
			const string defaultcase = @"LOG(""Unexpected enum value: %s->%s=%s\n"", svg->att, svg->att, svg->value);";
			if (elts.Count() > 1) {
				bool chainTest = false;
				if (chain.Length > 0) {
					if (chain.Length == 1)
						tw.WriteLine ($"if (tolower(svg->value[{level-chain.Length}]) == \'{chain[0]}\') {{//up");
					else
						tw.WriteLine ($"if (!strncasecmp (&svg->value[{level-chain.Length}],\"{chain.ToString()}\",{chain.Length})) {{//up");
					tw.Indent++;
					chain.Clear();
					chainTest = true;
				}
				tw.WriteLine ($"switch(tolower(svg->value[{level}])) {{");
				foreach (IGrouping<char, string> elt in elts) {
					tw.WriteLine ($"case '{char.ToLower(elt.Key)}':");
					if (elt.Count() == 1) {
						tw.Indent++;
						process_enum_match (tw, adi, elt.First());
						tw.WriteLine ($"break;");
						tw.Indent--;
						continue;
					}
					string ed = elt.FirstOrDefault (e=>e.Length == level + 1);
					if (ed != null) {
						tw.Indent++;
						tw.WriteLine ($"if (nameLenght == {level + 1})");
						tw.Indent++;
						process_enum_match (tw, adi, ed);
						tw.Indent--;
						tw.WriteLine (@"else {");
						tw.Indent++;
						process_enums (adi, level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e[level+1]), chain);
						tw.Indent--;
						tw.WriteLine (@"}");
					} else {
						tw.Indent++;
						process_enums (adi, level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
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
				IGrouping<char, string> elt = elts.First();
				if (elt.Count() == 1) {
					string c = elt.First();
					tw.WriteLine ($"if (!strcasecmp (&svg->value[{level}],\"{c.Substring(level).ToLower()}\"))");
					tw.Indent++;
					process_enum_match (tw, adi, c);
					tw.Indent--;
					tw.WriteLine ($"else");
					tw.Indent++;
					tw.WriteLine (defaultcase);
					tw.Indent--;
					return;
				}
				chain.Append (char.ToLower(elt.Key));
				string ed = elt.FirstOrDefault (e=>e.Length == level + 1);
				if (ed != null) {
					if (chain.Length == 1)
						tw.WriteLine ($"if (tolower(svg->value[{level-chain.Length+1}]) == \'{chain[0]}\') {{//down");
					else
						tw.WriteLine ($"if (!strncasecmp (&svg->value[{level-chain.Length+1}],\"{chain.ToString()}\",{chain.Length})) {{//down");
					tw.Indent++;
					tw.WriteLine ($"if (nameLenght == {level + 1})");
					tw.Indent++;
					process_enum_match (tw, adi, ed);
					tw.Indent--;
					chain.Clear();
					tw.WriteLine (@"else {");
					tw.Indent++;
					process_enums (adi, level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e[level+1]), chain);
					tw.Indent--;
					tw.WriteLine (@"}");
					tw.Indent--;
					tw.WriteLine (@"} else");
					tw.Indent++;
					tw.WriteLine (defaultcase);
					tw.Indent--;
				} else
					process_enums (adi, level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
			}
		}
		static void process_attributes_match_enum (IndentedTextWriter tw, AttributeDefInfo adi, AttributeTypeDeclEnumerated atde) {
			IEnumerable<string> values = atde.tokenList.Select (tl=>tl.ToString());
			tw.WriteLine (@"{");
			tw.Indent++;
			tw.WriteLine (@"nameLenght = strlen (svg->value);");
			process_enums (adi, 0, tw, values.OrderBy (c=>c).GroupBy (e=>e[0]), new StringBuilder());
			tw.Indent--;
			tw.WriteLine (@"}");
		}
		static void process_attributes_match (IndentedTextWriter tw, AttributeDefInfo adi) {
			DefaultDecl defDecl = adi.attDef.defaultDecl.Extract<DefaultDecl>();
			if (adi.attDef.attributeTypeDecl is PEReference pe) {
				var tmp = pe.CompiledValue;
				if (tmp is AttributeTypeDeclEnumerated atde)
					process_attributes_match_enum (tw, adi, atde);
				else
					tw.WriteLine ($"PROCESS_{adi.define}");
			} else if (adi.attDef.attributeTypeDecl is AttributeTypeDeclEnumerated atde)
				process_attributes_match_enum (tw, adi, atde);
			else
				tw.WriteLine ($"PROCESS_{adi.define}");
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
						process_attributes_match (tw, elt.First());
						tw.WriteLine ($"break;");
						tw.Indent--;
						continue;
					}
					AttributeDefInfo ed = elt.FirstOrDefault (e=>e.name.Length == level + 1);
					if (ed != null) {
						tw.Indent++;
						tw.WriteLine ($"if (nameLenght == {level + 1})");
						tw.Indent++;
						process_attributes_match (tw, ed);
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
					process_attributes_match (tw, c);
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
					process_attributes_match (tw, ed);
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
			const string defaultcase = @"skip_element";
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
						tw.WriteLine ($"res = read_{elt.First()}_attributes ({commonSigCall});");
						tw.WriteLine ($"break;");
						tw.Indent--;
						continue;
					}
					string ed = elt.FirstOrDefault (e=>e.Length == level + 1);
					if (ed != null) {
						tw.Indent++;
						tw.WriteLine ($"if (nameLenght == {level + 1})");
						tw.Indent++;
						tw.WriteLine ($"res = read_{ed}_attributes ({commonSigCall});");
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
				IGrouping<char, string> elt = elts.First();
				if (elt.Count() == 1) {
					string c = elt.First();
					tw.WriteLine ($"if (!strcasecmp (&svg->elt[{level}],\"{c.Substring(level).ToLower()}\"))");
					tw.Indent++;
					tw.WriteLine ($"res = read_{c}_attributes ({commonSigCall});");
					tw.Indent--;
					tw.WriteLine ($"else");
					tw.Indent++;
					tw.WriteLine (defaultcase);
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
					tw.WriteLine ($"res = read_{ed}_attributes ({commonSigCall});");
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
					tw.WriteLine (defaultcase);
					tw.Indent--;
				} else
					process_elements (level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
			}
		}
		static string normalize (string str) => str.Replace ('-', '_');
		static string normalize_def (string str) => str.Replace ('-', '_').ToUpper();
		static string target_directory = @"generated";
		static string source_name = @"parser_gen";
		const string commonSig = "svg_context* svg, FILE* f, svg_attributes attribs, void* parentData";
		const string commonSigCall = "svg, f, attribs, parentData";


		static void write_process_attrib_macro (IndentedTextWriter tw, string define) {
			const string defaultattribdef = @"LOG(""Unprocessed attribute: %s->%s\n"", svg->elt, svg->att);";

			tw.WriteLine ($"#ifndef PROCESS_{define}");
			tw.Indent++;
			tw.WriteLine ($"#define PROCESS_{define} {defaultattribdef}");
			tw.Indent--;
			tw.WriteLine ($"#endif");
		}
		static void write_process_attrib_macro2 (IndentedTextWriter tw, string define) {
			tw.WriteLine ($"#define PROCESS_{define} ");
		}
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

			/*foreach (KeyValuePair<string, List<AttributeDefInfo>> kvp in attributes) {
				Console.WriteLine ($"{kvp.Key}");
				foreach (AttributeDefInfo adi in kvp.Value) {
					Console.WriteLine ($"\t{adi.attGroup}:{adi.attDef.Name}");
				}
			}*/

			Dictionary<string, List<string>> parenting = new Dictionary<string, List<string>>();

			foreach (ElementDecl e in dtd.Elements) {
				List<string> childs = new List<string>();
				ContentSpec cs = e.contentSpec.Extract<ContentSpec>();
				if (cs.contentType == ContentTypes.Empty)
					continue;
				contentSpecProcessing (cs, childs);
				parenting.Add (e.Name.ToString(), childs);
			}
			if (!Directory.Exists(target_directory))
				Directory.CreateDirectory(target_directory);

			string target_header = Path.Combine(target_directory, $"{source_name}.h");

			//string[] skip_funcs = {"rect","cirlce","svg","line","polygon","polyline","g","defs","stop","cirlce","path"};
			using (Stream stream = new FileStream (target_header, FileMode.Create)) {
				using (StreamWriter sw = new StreamWriter(stream)) {
					using (IndentedTextWriter tw = new IndentedTextWriter (sw, "\t")) {
						tw.WriteLine ("// autogenerated by https://github.com/jpbruyere/XMLToolsLibrary");
						tw.WriteLine ("// Copyright (c) 2022  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>");
						tw.WriteLine ("// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)\n");

						tw.WriteLine ($"#ifndef {source_name.ToUpper()}_H");
						tw.WriteLine ($"#define {source_name.ToUpper()}_H");
						tw.WriteLine ("#include \"vkvg_svg_internal.h\"\n");

						foreach (string group in attributes.Values.SelectMany (al=>al).Select (a=>a.attGroup_normalized).Distinct().Where(s=>s.EndsWith("ATTRIB"))) {
							tw.WriteLine ($"#ifndef HEADING_{group}");
							tw.Indent++;
							tw.WriteLine ($"#define HEADING_{group}");
							tw.Indent--;
							tw.WriteLine ($"#endif");
							tw.WriteLine ($"#ifndef PROCESS_{group}");
							tw.Indent++;
							tw.WriteLine ($"#define PROCESS_{group}");
							tw.Indent--;
							tw.WriteLine ($"#endif");
						}

						foreach (AttributeDefInfo adi in attributes.Values.SelectMany (al=>al).Distinct()) {
							DefaultDecl defDecl = adi.attDef.defaultDecl.Extract<DefaultDecl>();
							if (adi.attDef.attributeTypeDecl is PEReference pe) {
								var tmp = pe.CompiledValue;
								if (tmp is AttributeTypeDeclEnumerated atde) {
									foreach (string value in atde.tokenList.Select (tl=>tl.ToString()))
										write_process_attrib_macro (tw, $"{adi.define}_{value.ToUpper().Replace('-','_')}");
								} else
									write_process_attrib_macro (tw, adi.define);
							} else if (adi.attDef.attributeTypeDecl is AttributeTypeDeclEnumerated atde) {
								foreach (string value in atde.tokenList.Select (tl=>tl.ToString()))
									write_process_attrib_macro (tw, $"{adi.define}_{value.ToUpper().Replace('-','_')}");
							} else
								write_process_attrib_macro (tw, adi.define);
						}
						using (Stream stream2 = new FileStream ($"tmp.txt", FileMode.Create)) {
							using (StreamWriter sw2 = new StreamWriter(stream2)) {
								using (IndentedTextWriter tw2 = new IndentedTextWriter (sw2, "\t")) {

									foreach (AttributeDefInfo adi in attributes.Values.SelectMany (al=>al).Distinct()) {
										DefaultDecl defDecl = adi.attDef.defaultDecl.Extract<DefaultDecl>();
										if (adi.attDef.attributeTypeDecl is PEReference pe) {
											var tmp = pe.CompiledValue;
											if (tmp is AttributeTypeDeclEnumerated atde) {
												foreach (string value in atde.tokenList.Select (tl=>tl.ToString()))
													write_process_attrib_macro2 (tw2, $"{adi.define}_{value.ToUpper().Replace('-','_')}");
											} else
												write_process_attrib_macro2 (tw2, adi.define);
										} else if (adi.attDef.attributeTypeDecl is AttributeTypeDeclEnumerated atde) {
											foreach (string value in atde.tokenList.Select (tl=>tl.ToString()))
												write_process_attrib_macro2 (tw2, $"{adi.define}_{value.ToUpper().Replace('-','_')}");
										} else
											write_process_attrib_macro2 (tw2, adi.define);
									}
								}
							}
						}
						using (Stream stream2 = new FileStream ($"processgroups.txt", FileMode.Create)) {
							using (StreamWriter sw2 = new StreamWriter(stream2)) {
								using (IndentedTextWriter tw2 = new IndentedTextWriter (sw2, "\t")) {

									foreach (string group in attributes.Values.SelectMany (al=>al).Select (a=>a.attGroup_normalized).Distinct().Where(s=>s.EndsWith("ATTRIB"))) {
										tw2.WriteLine ($"#define PROCESS_{group}");
									}
								}
							}
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
						tw.WriteLine ($"#ifdef {source_name.ToUpper()}_IMPLEMENTATION");
						foreach (string elt in attributes.Keys) {
							/*if (skip_funcs.Contains(elt))
								continue;*/
							string elt_norm = normalize(elt);
							string elt_def = normalize_def(elt);
							List<AttributeDefInfo> adi = attributes[elt];
							tw.WriteLine ($"int read_{elt_norm}_attributes ({commonSig}) {{");
							tw.Indent++;
							tw.WriteLine ($"bool matchedId = false;");
							tw.WriteLine ($"HEADING_{elt_def}");
							foreach (string group in adi.Select (a=>a.attGroup_normalized).Distinct().Where(s=>s.EndsWith("ATTRIB"))) {
								tw.WriteLine ($"HEADING_{group}");
							}
							tw.WriteLine (@"read_attributes_loop_start");
							tw.Indent++;
							tw.WriteLine (@"int nameLenght = strlen (svg->att);");
							process_attributes (0, tw, adi.OrderBy (c=>c.name).GroupBy (e=>e.name[0]), new StringBuilder());
							tw.Indent--;
							tw.WriteLine (@"read_attributes_loop_end");
							foreach (string group in adi.Select (a=>a.attGroup_normalized).Distinct().Where(s=>s.EndsWith("ATTRIB"))) {
								tw.WriteLine ($"PROCESS_{group}");
							}
							tw.WriteLine ($"ELEMENT_PRE_PROCESS_{elt_def}");
							tw.WriteLine (@"read_tag_end");
							tw.WriteLine (@"if (res > 0)");
							tw.Indent++;
							if (parenting.ContainsKey (elt) && parenting[elt].Count > 0)
								tw.WriteLine ($"res = read_{elt_norm}_children ({commonSigCall});");
							else
								tw.WriteLine ($"res = skip_children ({commonSigCall});");
							tw.Indent--;
							tw.WriteLine ($"ELEMENT_POST_PROCESS_{elt_def}");
							tw.WriteLine (@"if (matchedId)");
							tw.Indent++;
							tw.WriteLine (@"svg->skipDraw = true;");
							tw.Indent--;
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

							tw.WriteLine (@"while (!feof (f)) {");
							tw.Indent++;

							tw.WriteLine (@"read_element_start");

							tw.WriteLine (@"int nameLenght = strlen (svg->elt);");

							process_elements (0,tw, kvp.Value.OrderBy (c=>c).GroupBy (e=>e[0]), new StringBuilder());

							tw.Indent--;
							tw.WriteLine (@"}");
							tw.WriteLine (@"return res;");
							tw.Indent--;
							tw.WriteLine (@"}");
						}
						tw.WriteLine (@"#endif");
					}
				}
			}

			Console.WriteLine ($"svg header written to: {Path.GetFullPath(target_header)}");
		}
	}
}
