using System;
using System.Collections.Generic;
using System.Linq;
using XMLTools;

namespace svgparsergen
{
	class Program
	{
		static void extractAttlistDecl (AttlistDecl ad, List<AttlistDecl> otherAttlistDecl) {
			foreach (XMLToken item in ad.attributeDef) {
				if (item is PEReference) {
					AttlistDecl alDecl = item.Extract<AttlistDecl> ();
					if (alDecl != null && ! otherAttlistDecl.Any (oad=>oad.Name == alDecl.Name)) {
						otherAttlistDecl.Add (alDecl);
						extractAttlistDecl (alDecl, otherAttlistDecl);
					}

				}
			}
		}
		static void extractContentSpec (ContentSpec cs, Dictionary<string, ContentSpec> othercs) {
			if (cs is Particle p) {
				foreach (XMLToken c in p.Children) {
					if (c is PEReference pe) {
						var tmp = pe.CompiledValue;
						ContentSpec ocs = c.Extract<ContentSpec>();
						if (ocs != null && !othercs.ContainsKey(pe.CompiledName)) {
							othercs.Add (pe.CompiledName, ocs);
							extractContentSpec (ocs, othercs);
						}
					}
				}
			}
		}
		static void attlistDeclProcessing (AttlistDecl ad) {
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine ($"{ad.Name}");
			Console.ResetColor();
			foreach (XMLToken item in ad.attributeDef) {
				if (item is AttributeDef adef) {
					Console.ForegroundColor = ConsoleColor.DarkYellow;
					DefaultDecl dd = adef.defaultDecl.Extract<DefaultDecl>();
					Console.Write($"\t{adef.Name,-40}");
					Console.Write($"{dd.type,-10}{dd.DefaultValue,-20}");
					if (adef.attributeTypeDecl is PEReference pe)
						Console.WriteLine($"pe:{pe.CompiledName} {pe.CompiledValue}");
					else if (adef.attributeTypeDecl is AttributeTypeDeclEnumerated atde)
						Console.WriteLine($"enum:{atde}");
					else
						Console.WriteLine($"{adef.attributeTypeDecl}");
					Console.ResetColor();
				} else {
					Console.ForegroundColor = ConsoleColor.Magenta;
					Console.WriteLine ($"\t{item.CompiledName,-40}{(item as PEReference).CompiledValue.GetType().Name}");
					Console.ResetColor();
				}
			}

		}
		static void contentSpecProcessing (ContentSpec cs) {
			Console.ForegroundColor = ConsoleColor.DarkGreen;
			Console.WriteLine ($"\t{cs.contentType,-20}opt:{cs.IsOptional,-8}uni:{cs.IsUnique,-8}{cs.GetType().Name}");
			if (cs.contentType == ContentTypes.Empty)
				return;
			if (cs is Particle p) {
				foreach (XMLToken c in p.Children) {
					if (c is PEReference pe) {
						Console.ForegroundColor = ConsoleColor.Yellow;
						Console.WriteLine ($"\t\tpe:{c.CompiledName,-30}{pe.Value}");
					} else {
						Console.ForegroundColor = ConsoleColor.Gray;
						Console.WriteLine ($"\t\t{cs}");
					}
				}
			} else {
				Console.ForegroundColor = ConsoleColor.DarkYellow;
				Console.WriteLine ($"\t\t{cs.contentType}");
			}
		}
		static void elementDeclProcessing (ElementDecl ed) {
			Console.ForegroundColor = ConsoleColor.Green;
			Console.WriteLine ($"{ed.CompiledName,-40} {ed.Name}");
			ContentSpec cs = ed.contentSpec.Extract<ContentSpec>();
			contentSpecProcessing (cs);
			Console.ResetColor();
		}

		static void Main(string[] args)
		{
			string test = System.IO.Directory.GetCurrentDirectory();
			XMLDocument dtd = new XMLDocument ("data/svg11-flat.dtd");

			Dictionary<string, ContentSpec> othercs = new Dictionary<string, ContentSpec>();
			foreach (ElementDecl e in dtd.Elements) {
				extractContentSpec (e.contentSpec.Extract<ContentSpec>(), othercs);
			}

			foreach (ElementDecl e in dtd.Elements)
				elementDeclProcessing (e);
			Console.ForegroundColor = ConsoleColor.DarkBlue;
			Console.WriteLine ($"=========== other ================");

			foreach (KeyValuePair<string,ContentSpec> cs in othercs) {
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.WriteLine(cs.Key);
				contentSpecProcessing (cs.Value);
			}
			Console.ResetColor();

			Console.ForegroundColor = ConsoleColor.DarkBlue;
			Console.WriteLine ($"=========== other ================");
			Console.WriteLine ($"=========== other ================");
			Console.WriteLine ($"=========== other ================");
			Console.WriteLine ($"=========== other ================");
			Console.ResetColor();

			List<AttlistDecl> otherAttlistDecl = new List<AttlistDecl>();
			foreach (AttlistDecl ad in dtd.Attributes)
				extractAttlistDecl (ad, otherAttlistDecl);

			foreach (AttlistDecl ad in dtd.Attributes)
				attlistDeclProcessing (ad);

			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.WriteLine ($"=========== other ================");
			Console.ResetColor();

			foreach (AttlistDecl ad in otherAttlistDecl)
				attlistDeclProcessing (ad);

		}
	}
}
