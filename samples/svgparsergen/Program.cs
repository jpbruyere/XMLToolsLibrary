using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using XMLTools;

namespace svgparsergen
{
	class Program
	{

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

		static void process (int level, IndentedTextWriter tw, IEnumerable<IGrouping<char, string>> elts, StringBuilder chain) {
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
						process (level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e[level+1]), chain);
						tw.Indent--;
						tw.WriteLine (@"}");
					} else {
						tw.Indent++;
						process (level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
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
					process (level+1, tw, elt.Where(el => el != ed). GroupBy (e=>e[level+1]), chain);
					tw.Indent--;
					tw.WriteLine (@"}");
					tw.Indent--;
					tw.WriteLine (@"} else");
					tw.Indent++;
					tw.WriteLine ($"skip_element");
					tw.Indent--;
				} else
					process (level+1, tw, elt.GroupBy (e=>e[level+1]), chain);
			}
		}
		static string normalize (string str) => str.Replace ('-', '_');

		static void Main(string[] args)
		{
			string test = System.IO.Directory.GetCurrentDirectory();
			XMLDocument dtd = new XMLDocument ("data/svg11-flat.dtd");

			Dictionary<string, List<string>> parenting = new Dictionary<string, List<string>>();

			foreach (ElementDecl e in dtd.Elements) {
				List<string> childs = new List<string>();
				ContentSpec cs = e.contentSpec.Extract<ContentSpec>();
				if (cs.contentType == ContentTypes.Empty)
					continue;
				contentSpecProcessing (cs, childs);
				parenting.Add (normalize(e.Name.ToString()), childs);
			}

			const string commonSig = "svg_context* svg, FILE* f, svg_paint_type hasStroke, svg_paint_type hasFill, uint32_t stroke, uint32_t fill";

			//string[] skip_funcs = {"rect","cirlce","svg","line","polygon","polyline","g","defs","stop","cirlce","path"};

			using (Stream stream = new FileStream ("/mnt/devel/tests/svgParser/src/parser_gen.c", FileMode.Create)) {
				using (StreamWriter sw = new StreamWriter(stream)) {
					using (IndentedTextWriter tw = new IndentedTextWriter (sw, "\t")) {
						tw.WriteLine ("#include \"vkvg_svg_internal.h\"\n");

						foreach (string elt in parenting.Keys) {
							/*if (skip_funcs.Contains(elt))
								continue;*/
							tw.WriteLine ($"int read_{elt}_attributes ({commonSig}) {{");
							tw.WriteLine (@"}");
						}
						tw.WriteLine ("\n");

						foreach (KeyValuePair<string, List<string>> kvp in parenting) {
							if (kvp.Value.Count == 0)
								continue;
							tw.WriteLine ($"int read_{kvp.Key}_children ({commonSig}) {{");
							tw.Indent++;

							tw.WriteLine (@"int res = 0;");
							tw.WriteLine (@"int nameLenght = strlen (svg->elt);");

							tw.WriteLine (@"while (!feof (f)) {");
							tw.Indent++;

							tw.WriteLine (@"read_element_start");

							process (0,tw, kvp.Value.OrderBy (c=>c).GroupBy (e=>e[0]), new StringBuilder());

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
