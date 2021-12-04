// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

//[15] Comment ::= '<!--' ((Char - '-') | ('-' (Char - '-')))* '-->'

namespace XMLTools
{
    public class Comment : DTDObject
    {
        private string _Content = "";

        public string Content
        {
            get { return _Content; }
            set { _Content = value; }
        }

        internal static Comment Parse(XMLParser parser)
        {
            Comment c = "";

            while (!parser.EndOfStream && !c.Content.EndsWith("--"))
                c.Content += parser.nextChar;

            if (c.Content.EndsWith("--"))
            {
                if (parser.TestNextChar('>',true))
                    c.Content = c.Content.Remove(c.Content.Length - 2);
                else
                    throw new XMLParserException("'--' sequence not allowed in comments.");
            }
            else
                throw new XMLParserException("comment closing sequence ('-->') not found.");

            return c;
        }

        public string xml()
        {
            return string.Format("<!--{0}--!>", Content);
        }
        public static implicit operator string(Comment c)
        { return c.Content; }
        public static implicit operator Comment(string s)
        { return new Comment { Content = s }; }
    }
}
