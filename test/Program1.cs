//
// Program.cs
//
// Author:
//       Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// Copyright (c) 2019 
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
using System;
using System.Linq;
using XMLTools;

namespace test {
	class MainClass {
		public static void Main (string[] args) {
			XMLDocument dtd = new XMLDocument ("svg11-flat.dtd");
			//CCodeGenerator.Process (dtd);
			foreach (ElementDecl e in dtd.Elements) {
				string name = e.Name.Value;
				Console.WriteLine (name);

				AttlistDecl ad = dtd.Attributes.Where (a=> a.Name.Value == name).FirstOrDefault();
				AttlistDeclProcessing (ad);
			}
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
