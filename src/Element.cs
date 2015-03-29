using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

/*
[45]   	elementdecl	    ::=   	'<!ELEMENT' S Name S contentspec S? '>'	[VC: Unique Element Type Declaration]
[46]   	contentspec	    ::=   	'EMPTY' | 'ANY' | Mixed | children 
[47]   	children	    ::=   	(choice | seq) ('?' | '*' | '+')?
[48]   	cp	            ::=   	(Name | choice | seq) ('?' | '*' | '+')?
[49]   	choice	        ::=   	'(' S? cp ( S? '|' S? cp )+ S? ')'	[VC: Proper Group/PE Nesting]
[50]   	seq	            ::=   	'(' S? cp ( S? ',' S? cp )* S? ')'
[51]   	Mixed	        ::=   	'(' S? '#PCDATA' (S? '|' S? Name)* S? ')*'
			                  | '(' S? '#PCDATA' S? ')'
/*
[39]   	element	        ::=   	EmptyElemTag
			                    | STag content ETag 	        [WFC: Element Type Match]
				                                                [VC: Element Valid]
[43]   	content	        ::=   	CharData? ((element | Reference | CDSect | PI | Comment) CharData?)*
[44]   	EmptyElemTag    ::=   	'<' Name (S Attribute)* S? '/>'	[WFC: Unique Att Spec]
*/


namespace XMLTools
{
    /// <summary>
    /// Content particle type, Sequence of Choice
    /// </summary>
    public enum ParticleTypes
    {
        Unknown,        
        Choice,
        Sequence,
    }

    /// <summary>
    /// Element declaration
    /// </summary>
    public class ElementDecl : DTDObject
    {
        private XMLToken _contentSpec;
        public XMLToken contentSpec
        {
            get { return _contentSpec; }
            set { _contentSpec = value; }
        }

        internal static XMLToken parse(XMLParser reader)
        {
            //1)create token
            ElementDecl ed = new ElementDecl();
            //2)push token on the stack
            reader.DTDObjectStack.Push(ed);

            ed.Name = reader.nextNameToken;

            foreach (DTDObject o in reader.XMLObjects)
                    if (o.Name.ToString() == ed.Name.ToString() && o is ElementDecl)
                        throw new XMLParserException("Element already defined:" + ed.Name.ToString());


            ed.contentSpec = ContentSpec.parse(reader);

            return reader.PopDTDObj();
        }
        /// <summary>
        /// dump element in DTD syntax with resolved PERef
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return string.Format("<!ELEMENT {0} {1}>", Name, contentSpec.ToString());
        }
    }
    public enum ContentTypes
    {
        Any,
        Empty,
        Children,
        Mixed
    }

    /// <summary>
    /// base class for content spec from which derived special type ANY, Empty, etc...
    /// </summary>
    public class ContentSpec : ParticleBase
    {
        public ContentTypes contentType;
        public override string ToString()
        {
            switch (contentType)
            {
                case ContentTypes.Any:
                    return "ANY";
                case ContentTypes.Empty:
                    return "EMPTY";
                default:
                    return "Error";
            }
        }
        public static XMLToken parse(XMLParser xp)
        {
            //1) create Token to put on the stack
            ContentSpec cs = new ContentSpec();
            //2) push it on DTDObjects stack
            xp.DTDObjectStack.Push(cs);
            //3) process whitespaces skip during which some parameter entity reference could be encounter
            xp.skipWhiteSpaces();

            switch (xp.peekChar)
            {
                case '(':
                    //skip the parenthesis
                    xp.Read();
                    //the content spec is a particle
                    //remove fake token from the stack without altering highestLevel in posiitoningStack                                        
                    //create and push the final ParticleObject
                    cs = new Particle();
                    xp.ReplaceTopOfTheStack(cs);
                    
                    xp.skipWhiteSpaces();

                    if (xp.TestNextChar('#', false))
                    {
                        #region #PCDATA
                        xp.Read();
                        Char[] tmp = new Char[6];

                        try
                        {
                            xp.Read(tmp, 0, 6);
                        }
                        catch (Exception e)
                        {
                            throw new XMLParserException("parsing error, expecting #PCDATA", e);
                        }

                        if (string.Compare(new string(tmp), "PCDATA") != 0)
                            throw new XMLParserException("expecting #PCDATA");

                        cs.contentType = ContentTypes.Mixed;
                        #endregion
                    }
                    else
                        cs.contentType = ContentTypes.Children;

                    Particle.parse(xp);
                    break;
                default:
                    //should be ANY or EMPTY
                    switch (xp.nextNameToken)   //c'est pas vraiment un nom, mais ca ira
                    {
                        case "ANY":
                            cs.contentType = ContentTypes.Any;
                            break;
                        case "EMPTY":
                            cs.contentType = ContentTypes.Empty;
                            break;
                        default:
                            throw new XMLParserException("error parsing element content specification");
                    }
                    break;
            }
            //ensure all PEResolution are finished before returning main object
            xp.skipWhiteSpaces();
            return xp.PopDTDObj();
        }
    }

    public class ParticleBase : XMLToken
    {
        private bool _IsOptional = false;
        private bool _IsUnique = true;

        public bool IsOptional
        {
            get { return _IsOptional; }
            set { _IsOptional = value; }
        }
        public bool IsUnique
        {
            get { return _IsUnique; }
            set { _IsUnique = value; }
        }

        internal void checkForOccurenceModificator(XMLParser reader)
        {
            switch (reader.peekChar)
            {
                case '?':
                    reader.Read();
                    IsUnique = true;
                    IsOptional = true;
                    break;
                case '+':
                    reader.Read();
                    IsOptional = false;
                    IsUnique = false;
                    break;
                case '*':
                    reader.Read();
                    IsOptional = true;
                    IsUnique = false;
                    break;
            }
        }

        public XMLToken Name
        {
            get
            {
                return value;
            }
            set { base.value = value; }
        }

        public override string ToString()
        {
            string tmp = Name.ToString();

            if (IsOptional)
            {
                if (IsUnique)
                    tmp += "?";
                else
                    tmp += "*";
            }
            else
                if (!IsUnique)
                    tmp += "+";
            return tmp;
        }
    }

    public class Particle : ContentSpec
    {
        internal Particle Parent;

        private List<XMLToken> _Children = new List<XMLToken>();
        public List<XMLToken> Children
        {
            get { return _Children; }
            set { _Children = value; }
        }
        
        public static void parse(XMLParser reader)
        {
            while (!reader.EndOfStream)
            {
                reader.skipWhiteSpaces();

                Particle cp = reader.topOfDTDStack as Particle;

                switch (reader.peekChar)
                {
                    case '(':
                        reader.Read();                        
                        reader.DTDObjectStack.Push(new Particle());
                        Particle.parse(reader);
                        break;
                    case ')':
                        reader.Read(); //skip closing ')'
                        if (reader.DTDObjectStack.Count > 2)
                            (reader.TopButOneOfDTDStack as Particle).addChild(reader.PopDTDObj());                        
                        (reader.topOfDTDStack as ParticleBase).checkForOccurenceModificator(reader);
                        return;
                    case '|':
                        reader.Read();
                        if (cp.ParticleType != ParticleTypes.Choice)
                            if (cp.ParticleType == ParticleTypes.Unknown)
                                cp.ParticleType = ParticleTypes.Choice;
                            else
                                throw new XMLParserException("Particle already set as " + cp.ParticleType.ToString());
                        break;
                    case ',':
                        reader.Read();
                        if (cp.ParticleType != ParticleTypes.Sequence)
                            if (cp.ParticleType == ParticleTypes.Unknown)
                                cp.ParticleType = ParticleTypes.Sequence;
                            else
                                throw new XMLParserException("Particle already set as " + cp.ParticleType.ToString());
                        break;
                    case '>':
                        return;
                    default:
                        ParticleBase pb = new ParticleBase();
                        reader.DTDObjectStack.Push(pb);
                        pb.Name = reader.nextNameToken;
                        (reader.TopButOneOfDTDStack as Particle).addChild(reader.PopDTDObj());
                        pb.checkForOccurenceModificator(reader);
                        break;
                }
            }
            throw new XMLParserException("error reading content spec for element");
        }

        public void addChild(XMLToken t)
        {
            Particle p = t as Particle;
            if (p != null)
                p.Parent = this;            

            Children.Add(t);
        }
        //true if on top op particle tree
        internal bool isContentSpecRoot
        {
            get { return Parent == null ? true : false; }
        }

        public bool IsEmpty
        {
            get { return (ParticleType == ParticleTypes.Unknown && Children.Count == 0) ? true : false; }
        }
        internal bool isCreatedByCompiler = false;                  //true if this was created for PE Resolution

        public ParticleTypes ParticleType = ParticleTypes.Unknown;

        public override string ToString()
        {
            String tmp = "";

            if (contentType == ContentTypes.Mixed)
                tmp = "(#PCDATA";

            if (Children.Count > 0)
            {
                    if (contentType == ContentTypes.Mixed)
                        tmp += " | ";
                    else if (isCreatedByCompiler)
                        tmp = " { ";
                    else
                        tmp = " ( ";                

                tmp += Children[0].ToString();

                for (int i = 1; i < Children.Count; i++)
                {
                    if (ParticleType == ParticleTypes.Choice)
                        tmp += " | ";
                    else if (ParticleType == ParticleTypes.Sequence)
                        tmp += " , ";
                    else
                        tmp += " $ ";
                    //Debugger.Break();
                    tmp += Children[i].ToString();
                }
                if (isCreatedByCompiler)
                    tmp += " } ";
                else
                    tmp += " ) ";
            }
            else if (contentType == ContentTypes.Mixed)
                tmp += " ) ";
            else
                tmp += Name.ToString();

            if (IsOptional)
            {
                if (IsUnique)
                    tmp += "?";
                else
                    tmp += "*";
            }
            else
                if (!IsUnique)
                    tmp += "+";


            return tmp;
        }
    }


    public class Element : node
    {
        public List<Attribute> Attributes = new List<Attribute>();

        public string getAttributeValue(string attributeName)
        {
            try
            {
                return Attributes.First(at => at.Name == "Type").Value;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public override string ToString()
        {
            string tmp = new string('\t', depth) + "<" + Name;
            if (Attributes.Count > 0)
                tmp += " " + Attributes.Select(at => at.ToString()).Aggregate((a, b) => a.ToString() + " " + b.ToString());

            if (Children.Count == 0)
                tmp += "/>";
            else
            {
                tmp += ">\n";
                foreach (Element e in Children.OfType<Element>())
                {
                    tmp += e.ToString() + "\n";
                }
                tmp += new string('\t', depth) + "</" + Name + ">";

            }
            return tmp;
        }
    }

}
