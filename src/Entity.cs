// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Linq;
/*

[70]   	EntityDecl	    ::=   	GEDecl | PEDecl
[71]   	GEDecl	        ::=   	'<!ENTITY' S Name S EntityDef S? '>'
[72]   	PEDecl	        ::=   	'<!ENTITY' S '%' S Name S PEDef S? '>'
[73]   	EntityDef	    ::=   	EntityValue | (ExternalID NDataDecl?)
[74]   	PEDef	        ::=   	EntityValue | ExternalID
[75]   	ExternalID	    ::=   	'SYSTEM' S SystemLiteral
			                  | 'PUBLIC' S PubidLiteral S SystemLiteral
[76]   	NDataDecl	    ::=   	S 'NDATA' S Name
*/

namespace XMLTools
{
    /// <summary>
    /// & General Entity,  process in XML
    /// % Parameter Entity, process in DTD
    /// </summary>
    public enum EntityDeclTypes
    {
        Unkwnown,
        GEDecl,
        PEDecl
    }
    /// <summary>
    /// Entity declaration
    /// </summary>
    public class EntityDecl : DTDObject
    {
        /// <summary>
        /// Entity type, GE of PE
        /// </summary>
        public EntityDeclTypes entityDeclType;
        /// <summary>
        ///
        /// </summary>
        public XMLToken Value;
        public XMLToken CompiledValue;
        /// <summary>
        /// position in the stream for reader goto and return
        /// </summary>
        public int line = 0;
        public int column = 0;
        public string fileName = "";    //file where entity is parser for the 1st time

        public static XMLToken parse(XMLParser reader)
        {
            reader.disableParameterEntityExpansion();
            reader.skipWhiteSpaces();

            EntityDecl entity = new EntityDecl();

            if (reader.TestNextChar('%'))
            {
                //parameter entity
                reader.Read();
                reader.skipWhiteSpaces();
                entity.entityDeclType = EntityDeclTypes.PEDecl;
            }
            else
                entity.entityDeclType = EntityDeclTypes.GEDecl;

            reader.restorePreviousParameterEntityExpansionStatus();

            entity.Name = reader.nextNameToken;

            entity.fileName = reader.FilePath;

            reader.skipWhiteSpaces();

            switch (reader.peekChar)
            {
                case '\'':
                case '"':
                    entity.line = reader.currentLine;
                    entity.column = reader.currentColumn + 1;
                    entity.Value = reader.nextEntityValue;
                    break;
                default:
                    entity.Value = ExternalID.Parse(reader);
                    break;
            }


            if ((entity.Value is ExternalID) && (entity.entityDeclType == EntityDeclTypes.GEDecl))
            {
                if (reader.nextWord == "NDATA")
                {
                    (entity.Value as ExternalID).NData = reader.nextValueToken;
                }
            }

            foreach (DTDObject o in reader.XMLObjects)
                if (o.Name.ToString() == entity.Name.ToString() && o is EntityDecl)
                    if ((o as EntityDecl).entityDeclType == entity.entityDeclType)
                        return null;

            return entity;
        }

        public bool IsValueEmpty
        {
            get { return string.IsNullOrEmpty(Value) && !(Value is ExternalID) ? true : false; }
        }

        public override string ToString()
        {
            string c = string.Empty;

            if (entityDeclType == EntityDeclTypes.PEDecl)
                c = "% ";

            if (Value is ExternalID)
                return string.Format("<!ENTITY " + c + "{0} {1}>", Name, Value);
            else
                return string.Format("<!ENTITY " + c + "{0} '{1}'>", Name, Value);
        }

        #region Parameter Entities Catégories
        //http://www.w3.org/TR/xhtml-modularization/dtd_module_rules.html

        public enum ParameterEntityCategories
        {
            Undefined,
            Mod,
            Module,
            QName,
            Content,
            Class,
            Mix,
            Attrib,
            //not present in standart, but well in dtd's
            Datatype,
            Attlist,
            Element,
            xmlns,
            Version,
            Prefixed,
            Prefix,

        }

        public ParameterEntityCategories Category
        {
            get
            {
				return entityDeclType != EntityDeclTypes.PEDecl ?
					ParameterEntityCategories.Undefined :
					GetCategoryFromPEName (Name);
            }
        }

        public string ModuleName
        {
            get
            {
				return GetModuleNameFromPEName (Name);
            }
        }
		/// <summary>
		/// compiled named based on entity declaration name
		/// </summary>
        public string ElementName
        {
            get
            {
				return GetElementNameFromPEName (Name);
            }
        }
		// Static helpers to decompose name in 'Module.names.category' from string
		// because PERef name could be used to name ATTLIST created by compiler
		public static string GetElementNameFromPEName(string _name)
		{
			if (_name == null)
				return null;

			String[] ts = _name.ToString().Split(new char[] { '.', '-' });

			if (ts.Length == 1)
				return _name;

			int starti = 0;
			int endi = ts.Count();

			if (!string.IsNullOrEmpty(GetModuleNameFromPEName(_name)))
				starti = 1;
			if (!(GetCategoryFromPEName(_name) == ParameterEntityCategories.Undefined))
				endi--;

			string tmp = "";
			for (int i = starti; i < endi; i++)
				tmp += char.ToUpper(ts[i][0]) + ts[i].Substring(1);

			//                if (tmp == CodeGenerator.CoreElementName)
			//                    tmp = CodeGenerator.elementBaseClass;
			//else if (Category == ParameterEntityCategories.QName || Category == ParameterEntityCategories.Content)
			//    tmp = ModuleName + tmp;

			return tmp;
		}
		public static string GetModuleNameFromPEName(string _name)
		{
			if (_name == null)
				return null;

			String[] ts = _name.ToString().Split(new char[] { '.' });
			ParameterEntityCategories cat = GetCategoryFromPEName (_name);

			if (cat == ParameterEntityCategories.Undefined)
			{
				if (ts.Count() < 2)
					return null;
			}
			else if (cat != ParameterEntityCategories.xmlns)
			{
				if (ts.Count() < 3)
					return null;
			}

			return ts[0];
		}
		public static ParameterEntityCategories GetCategoryFromPEName(string _name)
		{
			string suffixe = _name.ToString().Split(new char[] { '.' }).LastOrDefault();

			return
				Enum.GetNames(typeof(ParameterEntityCategories)).Contains(suffixe, StringComparer.OrdinalIgnoreCase) ?
				(ParameterEntityCategories)Enum.Parse(typeof(ParameterEntityCategories), suffixe, true) :
				ParameterEntityCategories.Undefined;
		}
        #endregion

		/// <summary>
		/// I think it creates DTD base object from Entity string with compiled value
		/// during parsing
		/// </summary>
        public bool ExtractXMLObject<T>(out T result)
        {
            if ((CompiledValue is T))
            {
                result = (T)Convert.ChangeType(CompiledValue, typeof(T));
                return result == null ? false : true;
            }
            //with 'out' keyword, result MUST be afected before returning
            result = (T)Activator.CreateInstance(typeof(T));

            return false;
        }
    }

    public class AttributeOrEntityValue
    {
        private string _Value = "";

        public string Value
        {
            get
            {
                if (XMLParser.ParameterEntityExpansion)
                {
                    string result = "";
                    int i = 0;
                    while (i < _Value.Length)
                    {
                        if (_Value[i] == '%')
                        {
                            i++;
                            string refName = "";
                            while (_Value[i] != ';')
                            {
                                refName += _Value[i];
                                i++;
                                if (i == _Value.Length)
                                    throw new Exception(string.Format("Invalid reference in entity {0} while resolving", refName));
                            }
                            //resolve
                            EntityDecl ed = XMLParser.CurrentParser.XMLObjects.OfType<EntityDecl>().FirstOrDefault(e => e.Name == refName);
                            if (ed == null)
                            {
                                throw new XMLParserException("Unable to resolve PEReference: %" + refName + ";");
                            }

                            result += ed.Value.ToString();
                        }
                        else
                            result += _Value[i];
                        i++;
                    }
                    return result;
                }
                else
                    return _Value;
            }
            set { _Value = value; }
        }

        public static implicit operator string(AttributeOrEntityValue sv)
        { return sv.Value; }
        public static implicit operator AttributeOrEntityValue(string s)
        { return new AttributeOrEntityValue { Value = s }; }

        public override string ToString()
        {
            return Value;
        }
    }
    public class GEReference
    {
        public string Value = "";
    }
    public class PEReference : XMLToken
    {
        public EntityDecl entityDecl = null;

        //TODO: create Position class to store those 3 values
        public long savedPosition = 0;  //absoluto pos after %PEref;
        public int savedCurLine = 0;    //saved
        public int savedCurColumn = 0;

        /// <summary>
        /// compiled form in context
        /// </summary>
        private XMLToken _compiledValue;

        public XMLToken CompiledValue
        {
            get { return _compiledValue; }
            set
            {
                _compiledValue = value;
                RegisterCompiledValue(value);
            }
        }
        /// <summary>
        /// store compiled value in entity declaration
        /// </summary>
        /// <param name="t"></param>
        public void RegisterCompiledValue(XMLToken t)
        {
            if (entityDecl == null)
                throw new XMLParserException("Unable to register PEReference compilation result: EntityDecl not found");

            entityDecl.CompiledValue = t;
        }

        public override string CompiledName
        {
            get
            {
                return entityDecl.CompiledName;
            }
        }
        PEReference()
        {
        }


        public PEReference(string Name)
        {
            //search parsed Entity for a reference matching
            EntityDecl ed = XMLParser.CurrentParser.XMLObjects.OfType<EntityDecl>().
                FirstOrDefault(e => e.Name == Name && e.entityDeclType == EntityDeclTypes.PEDecl);

            if (ed == null)
                throw new XMLParserException("Unable to resolve PEReference: %" + Name + ";");

            entityDecl = ed;
        }
        public static implicit operator string(PEReference peRef)
        {
            return peRef.ToString();
        }

        public static implicit operator PEReference(string s)
        { return new PEReference(s); }

        public static implicit operator AttlistDecl(PEReference peRef)
        { return peRef == null ? null : peRef.CompiledValue as AttlistDecl; }

        public static implicit operator AttributeDef(PEReference peRef)
        { return peRef == null ? null : peRef.CompiledValue as AttributeDef; }

        public static implicit operator Particle(PEReference peRef)
        { return peRef == null ? null : peRef.CompiledValue as Particle; }

        public static implicit operator ContentSpec(PEReference peRef)
        { return peRef == null ? null : peRef.CompiledValue as ContentSpec; }

        public static implicit operator ParticleBase(PEReference peRef)
        { return peRef == null ? null : peRef.CompiledValue as ParticleBase; }

        public override string ToString()
        {
            if (!XMLParser.ParameterEntityExpansion)
                return "%" + entityDecl.Name + ";"; ;

            if (CompiledValue == null)
                return "[entityDecl.Value]";

            return CompiledValue.ToString();
        }
    }
    public class CharRef : GEReference
    {
        public bool IsHexadecimal = false;
    }

    public enum ExternalIDTypes
    {
        PUBLIC,
        SYSTEM
    }
    public class ExternalID : XMLToken
    {
        public string System = "";
        public string PublicId = "";
        public string NData = "";

        public static ExternalID Parse(XMLParser parser)
        {
            switch (parser.nextWord)
            {
                case "SYSTEM":
                    parser.skipWhiteSpaces();  //only one space allawed i think
                    return new ExternalID { System = parser.nextSystemLiteral };
                case "PUBLIC":
                    ExternalID extID = new ExternalID();
                    parser.skipWhiteSpaces();
                    extID.PublicId = parser.nextPubidLiteral;
                    parser.skipWhiteSpaces();
                    extID.System = parser.nextSystemLiteral;
                    return extID;
                default:
                    return null;
            }
        }

        public override string ToString()
        {
            string tmp = "";

            if (string.IsNullOrEmpty(PublicId))
                tmp = string.Format("SYSTEM '{0}'", System);
            else
            {
                tmp = string.Format("PUBLIC '{0}'", PublicId);
                if (!string.IsNullOrEmpty(System))
                    tmp += " '" + System + "'";
            }

            if (!string.IsNullOrEmpty(NData))
                tmp += string.Format(" NDATA '{0}'", NData);
            return tmp;
        }
    }


}



//intéressant comme premier jet
//public class Entity : DTDObject
//{
//    public const string VALID_CHARS = "";

//    public enum types
//    {
//        INTERNAL_PARSED,
//        EXTERNAL_PARSED,
//        EXTERNAL_UNPARSED
//    }
//    public string Name;
//    public string Uri;
//    public string Value;
//}
