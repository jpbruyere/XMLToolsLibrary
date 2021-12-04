// Copyright (c) 2019-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Linq;
using XMLTools;

namespace test {
	class MainClass {

		static void process (int level, IEnumerable<IGrouping<char, ElementDecl>> elts) {
			foreach (IGrouping<char, ElementDecl> elt in elts) {
				Console.WriteLine ($"{new string(' ', level)} {level}: {elt.Key}");
				if (elt.Count() == 1) {
					Console.WriteLine ($"{new string(' ', level + 1)} {level} -> {elt.First().Name.Value}");
					continue;
				}
				ElementDecl ed = elt.FirstOrDefault (e=>e.Name.Value.Length == level + 1);
				if (ed != null) {
					Console.WriteLine ($"{new string(' ', level + 1)} {level} -> {ed.Name.Value}");
					process (level+1, elt.Where(el => el != ed). GroupBy (e=>e.Name.Value[level+1]));
				} else
					process (level+1, elt.GroupBy (e=>e.Name.Value[level+1]));
			}
		}
		public static void Main (string[] args) {
			XMLDocument dtd = new XMLDocument ("svg11-flat.dtd");
			//CCodeGenerator.Process (dtd);
			foreach (ElementDecl e in dtd.Elements) {
				string name = e.Name.Value;
				Console.WriteLine (name);

				AttlistDecl ad = dtd.Attributes.Where (a=> a.Name.Value == name).FirstOrDefault();
				AttlistDeclProcessing (ad);
			}
			//process (0, dtd.Elements.OrderBy (eo=>eo.Name.Value).GroupBy (e=>e.Name.Value[0]));

			/*foreach (var elt in elts) {
				Console.WriteLine ($"-{elt.Key}");
				foreach (ElementDecl ed in elt.Where (e=>e.Name.Value.Length == 1))
					Console.WriteLine ($"\t{ed.Name.Value}");

				var elts2 = elt.Where (e=>e.Name.Value.Length > 1).GroupBy (e=>e.Name.Value[1]);
				foreach (var ed in elts2) {
					Console.WriteLine ($"\t-{ed.Key}");
					foreach (ElementDecl ed2 in ed)
						Console.WriteLine ($"\t\t{ed2.Name.Value}");
				}
			}*/
		}
		static string parseAttributes = @"
int parse_attributes (FILE* pf, const wchar_t* cur_element, VkvgContext ctx) {
	wchar_t buff[1024];
	int buffLength=0;

	while (!feof (pf)){
		wchar_t c = (wchar_t)fgetwc (pf);
		switch (c) {
		case '>':
			parse_xml (pf, cur_element, ctx);
			return 0;
		case '/':
			get_next_wchar (pf, &c);
			if (c != '>'){
				perror (""Expecting '>'"");
				return -1;
			}
			return 0;
		default:
			if (wchar_is_space(&c))
				break;

			wchar_t valBuff[1024];
			int valBuffLength=0;

			//attribute
			if (!is_valid_name_start_char (c)){
				perror (""Invalid first character for name"");
				return -1;
			}
			//name
			buff[buffLength++] = c;
			while (get_next_wchar (pf, &c)) {
				if (wchar_is_space(&c) || c == '=')
					break;
				if (!is_valid_name_char (c)){
					perror (""Invalid character in name"");
					return -1;
				}
				buff[buffLength++] = c;
			}
			buff[buffLength++] = 0;
			//eq
			skip_space (pf, &c);
			if (c != '='){
				perror (""Expecting '='"");
				return -1;
			}
			get_next_wchar (pf, &c);
			skip_space (pf, &c);
			if (c == '""'){
				while (get_next_wchar(pf, &c)){
					if (c=='""')
						break;
					valBuff[valBuffLength++] = c;
				}
			} else if (c == '\''){
				while (get_next_wchar(pf, &c)){
					if (c=='\'')
						break;
					valBuff[valBuffLength++] = c;
				}
			}else {
				perror (""Expecting attribute value."");
				return -1;
			}
			valBuff[valBuffLength++] = 0;

			printf(""\t%ls = %ls\n"", buff, valBuff);
			buffLength = valBuffLength = 0;
			break;
		}
	}
}
		";
		static void AttlistDeclProcessing (AttlistDecl ad) {
			foreach (XMLToken item in ad.attributeDef) {
				AttlistDecl alDecl = item.Extract<AttlistDecl> ();

				if (alDecl == null) {
					AttributeDef a = item.Extract<AttributeDef> ();
					DefaultDecl dd = a.defaultDecl.Extract<DefaultDecl> ();

					Console.WriteLine ($"\t{a.CompiledName,-50} {a.attributeTypeDecl,-50} {a.attributeTypeDecl.CompiledName,-50} {dd}");
					//Console.WriteLine ($"\t{a}");
				} else {
					AttlistDeclProcessing (alDecl);
				}


			}

		}
	}
}
