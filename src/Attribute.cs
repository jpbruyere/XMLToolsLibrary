// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

/*

[53]   	AttDef	        ::=   	S Name S AttType S DefaultDecl
[54]   	AttType	        ::=   	StringType | TokenizedType | EnumeratedType
[55]   	StringType	    ::=   	'CDATA'
[56]   	TokenizedType	::=   	'ID'	    [VC: ID]
				                            [VC: One ID per Element Type]
				                            [VC: ID Attribute Default]
			                    | 'IDREF'	[VC: IDREF]
			                    | 'IDREFS'	[VC: IDREF]
			                    | 'ENTITY'	[VC: Entity Name]
			                    | 'ENTITIES'[VC: Entity Name]
			                    | 'NMTOKEN'	[VC: Name Token]
			                    | 'NMTOKENS'
[57]   	EnumeratedType  ::=   	NotationType | Enumeration
[58]   	NotationType	::=   	'NOTATION' S '(' S? Name (S? '|' S? Name)* S? ')' 	[VC: Notation Attributes]
				                                                                    [VC: One Notation Per Element Type]
				                                                                    [VC: No Notation on Empty Element]
				                                                                    [VC: No Duplicate Tokens]
[59]   	Enumeration	   ::=   	'(' S? Nmtoken (S? '|' S? Nmtoken)* S? ')'
[60]   	DefaultDecl	   ::=   	'#REQUIRED' | '#IMPLIED'
			                    | (('#FIXED' S)? AttValue)	[VC: Required Attribute]
				                                            [VC: Attribute Default Value Syntactically Correct]
				                                            [WFC: No < in Attribute Values]
				                                            [VC: Fixed Attribute Default]
				                                            [WFC: No External Entity References]


*/
namespace XMLTools
{
	public class Attribute
	{
		public string Name = "";
		public string Value = "";

		public override string ToString ()
		{
			return string.Format ("{0}='{1}'", Name, Value);
		}

		public static Attribute Parse (XMLParser parser)
		{
			parser.skipWhiteSpaces ();

			Attribute a = new Attribute ();

			a.Name = parser.nextName;

			if (string.IsNullOrEmpty (a.Name))
				return null;

			if (parser.nextChar != '=')
				throw new XMLParserException ("Invalid character in Attribute");

			a.Value = parser.nextAttValue;

			return a;
		}
	}

	public enum DefaultDeclTypes
	{
		NotSet,
		REQUIRED,
		IMPLIED,
		FIXED
	}
	//  [52]   	AttlistDecl	    ::=   	'<!ATTLIST' S Name AttDef* S? '>'
	public class AttlistDecl : DTDObject
	{
		public List<XMLToken> attributeDef = new List<XMLToken> ();
        internal bool isCreatedByCompiler = false;                  //true if this was created as supplementary attlist for PE Resolution
		internal static XMLToken parse (XMLParser reader)
		{

			AttlistDecl a = new AttlistDecl ();
			reader.DTDObjectStack.Push (a);

			a.Name = reader.nextNameToken;

            do
                AttributeDef.parse(reader);
            while (!reader.TestNextChar('>',false,true));

			return reader.PopDTDObj ();
		}

		public override string ToString ()
		{
            string tmp = "";
            if (!isCreatedByCompiler)
                tmp += string.Format("<!ATTLIST {0} \n", Name);
			foreach (XMLToken ad in attributeDef)
			{
				tmp += "\t" + ad.ToString () + "\n";
			}


			return isCreatedByCompiler ? tmp :  tmp + ">";
		}
	}

	public class DefaultDecl : XMLToken
	{
		public DefaultDeclTypes type = DefaultDeclTypes.NotSet;
		public XMLToken DefaultValue = null;

		public static void parse (XMLParser reader)
		{
            AttributeDef ad = reader.topOfDTDStack as AttributeDef;

			DefaultDecl dd = new DefaultDecl ();
			reader.DTDObjectStack.Push (dd);

			reader.skipWhiteSpaces ();

			//default-value
			if (reader.peekChar == '#')
			{
				reader.Read ();
				//validity constrain
				switch (reader.nextWord)
				{
					case "REQUIRED":
						dd.type = DefaultDeclTypes.REQUIRED;
						break;
					case "IMPLIED":     //no default value is provided
						dd.type = DefaultDeclTypes.IMPLIED;
						break;
					case "FIXED":
						dd.type = DefaultDeclTypes.FIXED;
						break;
					default:
						throw new XMLParserException ("Syntax Error");
				}
			}

			reader.skipWhiteSpaces (false);

			//read attribute type
			if (dd.type == DefaultDeclTypes.NotSet || dd.type == DefaultDeclTypes.FIXED)
				dd.DefaultValue = reader.nextAttValue;

            ad.defaultDecl = reader.PopDTDObj();
		}

		public override string ToString ()
		{
			switch (type)
			{
				case DefaultDeclTypes.IMPLIED:
					return "#IMPLIED";
				case DefaultDeclTypes.REQUIRED:
					return "#REQUIRED";
				case DefaultDeclTypes.FIXED:
					return string.Format ("#FIXED " + DefaultValue.ToString ());
				default:
					return DefaultValue.ToString ();
			}
		}
	}

	/// <summary>
	/// the base class is use as string type, defined only by the toString overide
	/// </summary>
	public class AttributeTypeDecl : XMLToken
	{
		public override string ToString ()
		{
			return "CDATA";
		}
	}

	public class AttributeTypeDeclTokenized : AttributeTypeDecl
	{
		public enum TokenizedTypes
		{
			ID,
			IDREF,
			ENTITY,
			ENTITIES,
			NMTOKEN,
			NMTOKENS
		}

		public TokenizedTypes type;

		public override string ToString ()
		{
			return type.ToString ();
		}
	}

	public class AttributeTypeDeclEnumerated : AttributeTypeDecl
	{
		public XMLToken notation = null;
		public List<XMLToken> tokenList = null;

		public override string ToString ()
		{
			string tmp = "";

			if (notation != null)
				tmp += notation.ToString();

			tmp += "(";
			foreach (XMLToken t in tokenList)
			{
				tmp += t;

				if (tokenList.IndexOf(t) == tokenList.Count - 1)
					tmp += ") ";
				else
					tmp += "|";
			}
			return tmp;
		}
	}

	public class AttributeDef : DTDObject
	{
		public XMLToken attributeTypeDecl = null;
		public XMLToken defaultDecl = null;

		internal static void parse (XMLParser reader)
		{
            //  >>  AttributeDef
			AttributeDef ad = new AttributeDef ();
			reader.DTDObjectStack.Push (ad);

			ad.Name = reader.nextNameToken;

            //if (ad.Name.ToString() == "xml:base")
            //    Debugger.Break();

            //trick to pass empty pe
            //TODO: find a better way
            if(string.IsNullOrEmpty(ad.Name.ToString()))
                if (reader.TestNextChar('>', false))
                {
                    //remove empty attdef on the stack
                    reader.DTDObjectStack.Pop();
                    return;
                }

            //  >>  AttType
			AttributeTypeDecl atd = new AttributeTypeDecl ();
			reader.DTDObjectStack.Push (atd);

			//skip ws after name with PE resolve on
			reader.skipWhiteSpaces ();



			//test for enumerated type
			if (Char.IsLetter (reader.peekChar))
			{
				XMLToken keyword = reader.nextNameToken;

				if (keyword == "NOTATION")
				{
					//replace current typeDecl by an enumerated one
					AttributeTypeDeclEnumerated atde = new AttributeTypeDeclEnumerated ();
                    atd = reader.ReplaceTopOfTheStack(atde) as AttributeTypeDecl;

					//retrieve notation name, TODO could have a special function with notation validity check
					atde.notation = reader.nextNameToken;

				}
                else if (Enum.GetNames (typeof(AttributeTypeDeclTokenized.TokenizedTypes)).Contains ((string)keyword))
                {
					//replace current type declaration by a tokenized one
                    AttributeTypeDeclTokenized atdt = new AttributeTypeDeclTokenized();
					atdt.type = (AttributeTypeDeclTokenized.TokenizedTypes)Enum.Parse (typeof(AttributeTypeDeclTokenized.TokenizedTypes), keyword);
                    reader.ReplaceTopOfTheStack(atdt);
                    //atd = atdt;
				}else if (keyword != "CDATA")
                    throw new XMLParserException("unexpected keyword '" + keyword + "' in attribute type declaration.");
            }

            if (reader.peekChar == '(')
			{
                #region enumeration parsing
                AttributeTypeDeclEnumerated atde = reader.topOfDTDStack as AttributeTypeDeclEnumerated;
                if (atde == null)
                {
                    atde = new AttributeTypeDeclEnumerated();
                    reader.ReplaceTopOfTheStack(atde);
                }

				atde.tokenList = new List<XMLToken> ();

				do
				{
					reader.Read ();
					atde.tokenList.Add (reader.nextNMTokenToken);
				} while (reader.TestNextChar('|'));

				if (!reader.TestNextChar (')', true))
					throw new XMLParserException ("')' expected ending attribute type enumeration.");
                #endregion
            } else if (atd is AttributeTypeDeclEnumerated)
				throw new XMLParserException ("Expected keyword opening parenthesys for attribute type enumeration.");

            //finishe typedecl pe if present
            reader.skipWhiteSpaces(false);

            //  <<  AttType
            (reader.TopButOneOfDTDStack as AttributeDef).attributeTypeDecl = reader.PopDTDObj();

            DefaultDecl.parse(reader);

            //  <<  AttributeDef
            XMLToken result = reader.PopDTDObj();

            reader.topOfDTDStack.Extract<AttlistDecl>().attributeDef.Add(result);

            //should clear positionning stack
            reader.skipWhiteSpaces(false);

		}

		public override string ToString ()
		{
			return string.Format("{0} {1} {2}",Name.ToString (),attributeTypeDecl.ToString(),defaultDecl.ToString ());

		}
	}
}
