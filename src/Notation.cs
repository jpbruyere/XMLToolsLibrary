// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

/*
[82]   	NotationDecl    = '<!NOTATION' S Name S (ExternalID | PublicID) S? '>'
[83]   	PublicId        = 'PUBLIC' S PubidLiteral
 */
namespace XMLTools
{
    class NotationDecl : DTDObject
    {
        public ExternalID Id { get; set; }
        public NotationDecl()
        {
            Id = new ExternalID();
        }
        public static XMLToken parse(XMLParser reader)
        {
            reader.skipWhiteSpaces();

            NotationDecl notation = new NotationDecl { Name = reader.nextNameToken };

            foreach (DTDObject o in reader.XMLObjects)
                if (o.Name.ToString() == notation.Name.ToString() && o is NotationDecl)
                    throw new XMLParserException("Element already defined:" + notation.Name.ToString());

            reader.skipWhiteSpaces();

            switch (reader.nextWord)
            {
                case "SYSTEM":
                    reader.skipWhiteSpaces();  //only one space allowed i think
                    notation.Id.System = reader.nextSystemLiteral;
                    break;
                case "PUBLIC":
                    reader.skipWhiteSpaces();  //only one space allowed i think
                    notation.Id.PublicId = reader.nextPubidLiteral;
                    reader.skipWhiteSpaces();  //only one space allawed i think
                    if (reader.nextCharIsDelimiter)
                        notation.Id.System = reader.nextSystemLiteral;
                    break;
            }

            return notation;
        }
    }
}
