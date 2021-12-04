// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;

/*
[28]   	doctypedecl	    ::=   	'<!DOCTYPE' S Name (S ExternalID)? S? ('[' intSubset ']' S?)? '>'	[VC: Root Element Type]
				                                                                                    [WFC: External Subset]
[28a]   	DeclSep	    ::=   	PEReference | S 	                                                [WFC: PE Between Declarations]
[28b]   	intSubset   ::=   	(markupdecl | DeclSep)*
[29]   	markupdecl	    ::=   	elementdecl | AttlistDecl | EntityDecl | NotationDecl | PI | Comment 	[VC: Proper Declaration/PE Nesting]
				                                                                                    [WFC: PEs in Internal Subset]
*/

namespace XMLTools {
	public class XMLDocument
    {
        public List<XMLToken> dtdObjects = new List<XMLToken>();

        /// <summary>
        /// LoadFromFile
        /// </summary>
        /// <param name="dtdPath">File path of the dtd</param>
        public XMLDocument(string dtdPath)
        {
            Stopwatch timer = Stopwatch.StartNew();

            load(dtdPath);

            timer.Stop();
            Console.WriteLine("Parsing time: ms:{0}  ticks:{1}", timer.ElapsedMilliseconds, timer.ElapsedTicks);
        }

        void load(string dtdPath)
        {
            if (!File.Exists(dtdPath))
                throw new Exception("File not found: " + dtdPath);

            //set working directory for external id resolution
            string dir = Path.GetDirectoryName(dtdPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.SetCurrentDirectory(dir);

            using (FileStream fs = new FileStream(dtdPath, System.IO.FileMode.Open))
            {
                using (XMLParser parser = new XMLParser(fs))
                {
                    parser.XMLObjects = dtdObjects;
                    parser.parse();

					//save the first doctype
					if (string.IsNullOrEmpty (rootElement))
						rootElement = parser.RootNodeName;
                }
            }

        }

        public ElementDecl[] Elements
        {
            get { return dtdObjects.OfType<ElementDecl>().ToArray(); }
        }
        public EntityDecl[] Entities
        {
            get { return dtdObjects.OfType<EntityDecl>().ToArray(); }
        }
        public AttlistDecl[] Attributes
        {
            get { return dtdObjects.OfType<AttlistDecl>().ToArray(); }
        }
		public string rootElement;

        public override string ToString()
        {
            XMLParser.ParameterEntityExpansion = true;

            string tmp = "";
            foreach (EntityDecl ent in dtdObjects.OfType<EntityDecl>())
            {
                tmp += ent.ToString() + "\n";
            }
            foreach (ElementDecl elt in dtdObjects.OfType<ElementDecl>())
            {
                tmp += elt.ToString() + "\n";
            }
            foreach (AttlistDecl ad in dtdObjects.OfType<AttlistDecl>())
            {
                tmp += ad.ToString() + "\n";
            }
            XMLParser.ParameterEntityExpansion = true;
            return tmp;
        }
    }


}
