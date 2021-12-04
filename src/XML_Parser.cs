// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;

/** TODO: add entity level

/*
[1]     document        ::= prolog element Misc*è&
[39]    element         ::= EmptyElemTag | STag content ETag    [WFC: Element Type Match]
                                                                [VC: Element Valid]
Extensible Markup Language (XML) 1.0 (Fifth Edition) 26/12/2013
http://
[77]   	TextDecl	    ::=   	'<?xml' VersionInfo? EncodingDecl S? '?>'

[22]   	prolog	        ::=   	XMLDecl? Misc* (doctypedecl Misc*)?
[23]   	XMLDecl	        ::=   	'<?xml' VersionInfo EncodingDecl? SDDecl? S? '?>'
[24]   	VersionInfo	    ::=   	S 'version' Eq ("'" VersionNum "'" | '"' VersionNum '"')
[25]   	Eq	            ::=   	S? '=' S?
[26]   	VersionNum	    ::=   	'1.' [0-9]+
[27]   	Misc	        ::=   	Comment | PI | S

[16]   	PI	            ::=   	'<?' PITarget (S (Char* - (Char* '?>' Char*)))? '?>'
[17]   	PITarget	    ::=   	Name - (('X' | 'x') ('M' | 'm') ('L' | 'l'))
[18]   	CDSect	        ::=   	CDStart CData CDEnd
[19]   	CDStart	        ::=   	'<![CDATA['
[20]   	CData	        ::=   	(Char* - (Char* ']]>' Char*))
[21]   	CDEnd	        ::=   	']]>'

[61]   	conditionalSect	::=   	includeSect | ignoreSect
[62]   	includeSect	    ::=   	'<![' S? 'INCLUDE' S? '[' extSubsetDecl ']]>' 	[VC: Proper Conditional Section/PE Nesting]
[63]   	ignoreSect	    ::=   	'<![' S? 'IGNORE' S? '[' ignoreSectContents* ']]>'	[VC: Proper Conditional Section/PE Nesting]
[64]   	ignoreSectContents ::=   	Ignore ('<![' ignoreSectContents ']]>' Ignore)*
[65]   	Ignore	        ::=   	Char* - (Char* ('<![' | ']]>') Char*)
 */

namespace XMLTools
{
    public class XMLParserException : Exception
    {
        public XMLParserException(string txt)
            : base(string.Format("XML Parser exception {3} line:{0}, column:{1} : {2}", XMLParser.CurrentParser.currentLine, XMLParser.CurrentParser.currentColumn, txt, XMLParser.externalFileProcessed))
        {
        }
        public XMLParserException(string txt, Exception innerException)
            : base(txt, innerException)
        {
            txt = string.Format("XML Parser exception {3} line:{0}, column{1} : {2}", XMLParser.CurrentParser.currentLine, XMLParser.CurrentParser.currentColumn, txt, XMLParser.externalFileProcessed);
        }
    }
    /* Processing Instruction (PI)
 * */
    public class PI
    {
        public string PITarget;
        public string Value;

        public override string ToString()
        {
            return string.Format("<?{0} {1}?>", PITarget, Value);
        }
        public static PI Parse(XMLParser parser)
        {
            PI pi = new PI();

            pi.PITarget = parser.nextName;

            string tmp = "";

            while (!tmp.EndsWith("?>"))
                tmp += parser.nextChar;

            pi.Value = tmp.Remove(tmp.Length - 2);

            return pi;
        }
    }
    public class XmlDecl
    {
        public List<Attribute> attributes = new List<Attribute>();

        public override string ToString()
        {
            string tmp = "<?xml";
            foreach (Attribute a in attributes)
            {
                tmp += string.Format(" {0}", a.ToString());
            }
            return tmp + "?>";
        }
    }
    public class DocTypeDecl
    {
        public string Name = "";
        public ExternalID extId = null;
        public XMLDocument dtdDocument;
    }


    public class XMLParser : StreamReader
    {
        public enum States
        {
            init,       //first statement of prolog, xmldecl should only apear in this state
            prolog,     //misc before doctypedecl
            InternalSubset,    //doctype declaration subset
            ExternalSubsetInit,
            ExternalSubset,
            DTDEnd,//doctype finished
            element,    //once parsed the root element until closing tag
            XMLEnd
        }
        enum Keywords
        {
            DOCTYPE,
            ELEMENT,
            ATTLIST,
            ENTITY,
            NOTATION
        }

        States currentState = States.init;

        public node currentNode = null;
        public node rootNode = null;


        public string RootNodeName = string.Empty;

        public List<XMLToken> XMLObjects = new List<XMLToken>();
        //TODO: implement standalone parameter
        public bool Standalone = false;
        public string FilePath = "";
        public XmlDecl XMLDeclaration = new XmlDecl();
        public ExternalID DTDExternalSubsetID = null;
        public List<ExternalID> DTDExternalModulesID = new List<ExternalID>();

        internal static bool ParameterEntityExpansion = true;
        internal static string externalFileProcessed = null;
        internal static XMLParser CurrentParser;

        #region DTDObjectsStack
        //track token currently parsed hierarchicaly and store during entity parsing the highest object reached
        //to determine what to store in the compiled form of the parameter entity
        internal Stack<XMLToken> DTDObjectStack = new Stack<XMLToken>();
        internal void PushDTDObj(ref XMLToken t)
        {
            DTDObjectStack.Push(t);
        }
        internal XMLToken PopDTDObj()
        {
            XMLToken tmp = DTDObjectStack.Pop();

            //on résoud toute les pe précédentes avec la même valeur de compilation,
            //si on désempile une ATTDEF, on sait que les peref avec la même valeur de compilation deviendront des
            //ATTLIST créée par le compilateur avec comme nom le nom de la peref
            //le problème est que l'on doit les empiler dans l'odre inverse de la boucle qui les détecte
            List<XMLToken> fakeATTLISTToAdd = new List<XMLToken>();

            for (int i = 0; i < PERefStack.Count; i++)
            {
                //si on désempile l'objet compilé de la PERef en cours
                //il faut remonter à l'objet précédent, et ce pour toute les peRef dont la valeur compilée est l'élément dépilé.
                PEReference peRef = PERefStack.ElementAt(i);
                if (peRef.CompiledValue == tmp)
                {
                    peRef.CompiledValue = topOfDTDStack;

                    AttributeDef ad = tmp.Extract<AttributeDef>();
                    if (ad != null)
                    {
                        //on crée une attliste propre à la pe si ce n'est déjà fait
                        AttlistDecl attdecl = topOfDTDStack as AttlistDecl;
                        if (attdecl == null)
                            Debugger.Break();
                        if (attdecl.Name != peRef.entityDecl.Name)
                        {
                            peRef.CompiledValue = new AttlistDecl
                            {
                                Name = peRef.entityDecl.Name,
                                isCreatedByCompiler = true
                            };
                            fakeATTLISTToAdd.Add(peRef.CompiledValue);
                        }
                        else
                            Debugger.Break();
                    }
                    else
                        createAdditionalLevelsIfNeeded();
                }
            }
            //push fake attlist on the stack if some have been created
            foreach (XMLToken t in fakeATTLISTToAdd)
                DTDObjectStack.Push(t);

            return tmp;
        }
        internal XMLToken topOfDTDStack
        {
            get { return DTDObjectStack.Count == 0 ? null : DTDObjectStack.Peek(); }
        }
        internal XMLToken TopButOneOfDTDStack
        {
            get { return DTDObjectStack.ElementAt(1); }
        }
        //take care if this has been setted as compiled form for last PERef
        internal XMLToken ReplaceTopOfTheStack(XMLToken newToken)
        {
            XMLToken tmp = DTDObjectStack.Pop();

            if (PERefStack.Count > 0)
            {
                //si on désempile l'objet compilé, il faut réaffecté le nouvel objet à la compilation
                PEReference peRef = PERefStack.Peek();
                if (peRef.CompiledValue == tmp)
                    peRef.CompiledValue = newToken;
            }
            DTDObjectStack.Push(newToken);
            return newToken;
        }
        #endregion

        public Stack<PEReference> PERefStack = new Stack<PEReference>();

        public XMLParser(Stream s)
            : base(s, true)
        {
        }


        /// <summary>
        /// MAIN parsing function
        /// </summary>
        public void parse(States initialState = States.init)
        {
            CurrentParser = this;

            RootNodeName = string.Empty;
            rootNode = null;
            currentState = initialState;
            currentNode = null;

            if (BaseStream is FileStream)
                FilePath = (BaseStream as FileStream).Name;

            //TODO: implement Coding detection
            //Ude.CharsetDetector cdet = new Ude.CharsetDetector();
            //cdet.Feed(this.BaseStream);
            //cdet.DataEnd();
            //if (cdet.Charset != null)
            //{
            //    Console.WriteLine("Charset: {0}, confidence: {1}",
            //         cdet.Charset, cdet.Confidence);
            //}
            //else
            //{
            //    Console.WriteLine("Detection failed.");
            //}

            currentPosition = CurrentEncoding.GetPreamble().Length;

			currentPosition = 0;
            currentLine = 1;
            currentColumn = 1;

            //Incremented each time a INCLUDE section is encountered to check for matching "]]>"
            int includeSectionOpenings = 0;

            while (!EndOfStream)
            {
                skipWhiteSpaces();

                if (EndOfStream)
                    break;

                switch (peekChar)
                {
                    case '<':
                        Read();
                        switch (peekChar)
                        {
                            case '/':
                                #region element closing tag
                                Read();

                                if (currentState != States.element)
                                    throw new XMLParserException("Unexpected character '/'");
                                if (nextName != currentNode.Name)
                                    throw new XMLParserException("Invalid closing tag, expecting: </" + currentNode.Name + ">");

                                currentNode = currentNode.Parent;

                                if (nextChar != '>')
                                    throw new XMLParserException("No closing tag");
                                break;
                                #endregion
                            case '?':
                                #region PI

                                Read();

                                PI pi = PI.Parse(this);//TODO, save PI's in an array
                                //check for XMLDecl, must be the first line if present
                                if (pi.PITarget.ToLower() == "xml")
                                {
                                    if (currentState == States.init)
                                        currentState = States.prolog;
                                    else if (currentState == States.ExternalSubsetInit)
                                        currentState = States.ExternalSubset;
                                    else
                                        throw new XMLParserException("xml Declaration '<?xml?> MUST be the first entity");

                                    //create parser for attribute parsing
                                    XMLParser attParser = new XMLParser(new MemoryStream(this.CurrentEncoding.GetBytes(pi.Value)));

                                    Attribute a = Attribute.Parse(attParser);
                                    if (a.Name == "version")
                                        this.XMLDeclaration.attributes.Add(a);
                                    else
                                        throw new XMLParserException("version attribute exptected in xml prolog");

                                    a = Attribute.Parse(attParser);
                                    if (a != null)
                                    {
                                        if (a.Name == "encoding")
                                            this.XMLDeclaration.attributes.Add(a);
                                        else
                                            throw new XMLParserException("encoding attribute exptected in xml prolog");
                                    }
                                }
                                break;
                                #endregion
                            case '!':
                                #region comments and sections
                                Read();
                                if (peekChar == '-')
                                {
                                    #region Comments processing

                                    disableParameterEntityExpansion();

                                    char[] buff = new char[2];

                                    Read(buff, 0, 2);
                                    if (string.Compare(new string(buff), "--") != 0)
                                        throw new XMLParserException("Comment Parsing exception, only one '-' in starting sequence");
                                    else
                                        XMLObjects.Add(Comment.Parse(this));

                                    restorePreviousParameterEntityExpansionStatus();

                                    #endregion
                                }
                                else if (peekChar == '[')
                                {
                                    #region special sections
                                    Read();

                                    string keyword = nextWord.ToString();

                                    if (keyword == "CDATA")
                                    {
                                        if ((Char)Read() != '[')
                                            throw new XMLParserException("'[' Expected");
                                        bypassCDATASection();
                                    }
                                    else if (currentState < States.DTDEnd)
                                    {
                                        switch (keyword)
                                        {
                                            case "IGNORE":
                                                if ((Char)Read() != '[')
                                                    throw new XMLParserException("'[' Expected");
                                                disableParameterEntityExpansion();
                                                bypassIgnoreSection();
                                                restorePreviousParameterEntityExpansionStatus();
                                                break;
                                            case "INCLUDE":
                                                if ((Char)Read() != '[')
                                                    throw new XMLParserException("'[' Expected");
                                                //should read normaly until "]]>"
                                                includeSectionOpenings++;
                                                break;
                                            default://9/9/2013...testing
                                                if ((Char)Read() != '[')
                                                    throw new XMLParserException("'[' Expected");
                                                //should read normaly until "]]>"
                                                includeSectionOpenings++;
                                                break;
                                        }

                                    }
                                    else
                                        throw new XMLParserException("'CDATA' keyword Expected");
                                    #endregion
                                }
                                else
                                {
                                    //reading keyword
                                    Keywords keyWord = (Keywords)Enum.Parse(typeof(Keywords), nextWord);

                                    if (keyWord == Keywords.DOCTYPE)
                                    {
                                        #region doctype declaration
                                        if (currentState > States.prolog)
                                            throw new XMLParserException("DOCTYPE declaration outside of prolog section");

                                        skipWhiteSpaces();
                                        RootNodeName = nextNameToken;
                                        skipWhiteSpaces();


                                        if (!TestNextChar('[', false))
                                            DTDExternalSubsetID = ExternalID.Parse(this);

                                        skipWhiteSpaces();

                                        switch (nextChar)
                                        {
                                            case '[':
                                                currentState = States.InternalSubset;
                                                break;
                                            case '>':
                                                if (DTDExternalSubsetID != null)
                                                    LoadSubset(DTDExternalSubsetID);

                                                foreach (ExternalID modID in DTDExternalModulesID)
                                                    LoadSubset(modID);

                                                currentState = States.DTDEnd;
                                                break;
                                            default:
                                                throw new XMLParserException("DOCTYPE closing '>' missing");
                                        }
                                        #endregion
                                    }
                                    else
                                    {
                                        if (currentState > States.ExternalSubset)
                                            throw new XMLParserException("DTD markup outside DOCTYPE ou external section");
                                        else if (currentState < States.InternalSubset)
                                            currentState = States.ExternalSubset;

                                        #region DTD makrup processing

                                        XMLToken obj = null;

                                        switch (keyWord)
                                        {
                                            case Keywords.ELEMENT:
                                                obj = ElementDecl.parse(this);
                                                break;
                                            case Keywords.ATTLIST:
                                                obj = AttlistDecl.parse(this);
                                                break;
                                            case Keywords.ENTITY:
                                                obj = EntityDecl.parse(this);
                                                break;
                                            case Keywords.NOTATION:
                                                obj = NotationDecl.parse(this);
                                                break;
                                            default:
                                                throw new XMLParserException("ELEMENT, ATTLIST or ENTITY keyword expected");
                                        }

                                        skipWhiteSpaces();

                                        if (!TestNextChar('>', true))
                                            throw new XMLParserException("closing > expected");

                                        if (obj != null)
                                            XMLObjects.Add(obj);

                                        #endregion
                                    }
                                }
                                break;
                                #endregion
                            default:
                                #region normal xml element parsing
                                if (nextCharIsValidCharStartName)
                                {
                                    //XML Elements processing
                                    node e = new node
                                    {
                                        Name = nextName
                                    };

                                    Attribute a = Attribute.Parse(this);
                                    while (a != null)
                                    {
                                        e.Attributes.Add(a);
                                        a = Attribute.Parse(this);
                                    }

                                    if (currentState == States.element)
                                        currentNode.addChild(e);
                                    else if (currentState > States.init)
                                        throw new XMLParserException("Unexpected xml in dtd markup");
                                    else
                                    {
                                        currentState = States.element;
                                        rootNode = e;
                                    }

                                    skipWhiteSpaces();

                                    switch (nextChar)
                                    {
                                        case '/'://empty element
                                            if (nextChar != '>')
                                                throw new XMLParserException("'>' expected after slash");
                                            break;
                                        case '>':
                                            currentNode = e;
                                            break;
                                        default:
                                            throw new XMLParserException("No closing tag ('>')");
                                    }
                                }
                                else
                                    throw new XMLParserException("unexpected character in tag name: '" + nextChar + "'");
                                #endregion
                                break;
                        }
                        break;
                    case ']':
                        Read();
                        switch (peekChar)
                        {
                            #region check for end of dtd or end of section
                            //TODO: check this!!!!!
                            case '>':
                                Read();
                                //make sense for end of doctype only
                                if (currentState == States.InternalSubset)
                                    currentState = States.DTDEnd;
                                else
                                    currentNode.content += "]>";
                                break;
                            case ']':
                                Read();
                                //check for end section ']]>'
                                if (peekChar == '>')
                                {
                                    Read();
                                    if (includeSectionOpenings > 0)
                                        includeSectionOpenings--;
                                }
                                else
                                    currentNode.content += "]]" + nextChar;
                                break;
                            default:
                                currentNode.content += "]" + nextChar;
                                break;
                            #endregion
                        }
                        break;
                    default:
                        if (currentState == States.element)
                            currentNode.content += nextChar;
                        else
                            throw new XMLParserException(string.Format("{1}: Unexpected character '{0}' ", nextChar, externalFileProcessed));
                        break;
                }
            }

            if (currentState == States.InternalSubset)
            {
                foreach (ExternalID modID in DTDExternalModulesID)
                    LoadSubset(modID);
            }

            CurrentParser = null;
        }

        #region PEResolution state on/off handling
        //temporary save resolution state while inhibiting
        bool resolutionStateSaved;
        internal void disableParameterEntityExpansion()
        {
            //inhib PERef resolution in ignore section
            resolutionStateSaved = ParameterEntityExpansion;
            ParameterEntityExpansion = false;
        }
        internal void restorePreviousParameterEntityExpansionStatus()
        {
            ParameterEntityExpansion = resolutionStateSaved;
        }
        #endregion

        #region position handling

        public long Position
        {
            get
            {
                return currentPosition;
            }
            set
            {
                if (PERefStack.Count == 0)
                {
                    BaseStream.Position = value;
                    DiscardBufferedData();
                    currentPosition = value;
                }
                else
                {
                    peRefReader.BaseStream.Position = value;
                    peRefReader.DiscardBufferedData();
                    currentPosition = value;
                }
            }
        }
        internal int currentLine = 0;
        internal int currentColumn = 0;
        internal long currentPosition = 0;


        internal void createAdditionalLevelsIfNeeded()
        {
            //si c'est un ContentParticle, on recré un niveau
            Particle tmp = topOfDTDStack as Particle;
            if (tmp != null)
            {
                PEReference peRef = PERefStack.Peek();

                peRef.CompiledValue = new Particle { isCreatedByCompiler = true };
                DTDObjectStack.Push(peRef.CompiledValue);
            }
        }

        internal void restoreLastPosition()
        {
            PEReference PERef = PERefStack.Pop();

            #region restore position

            if (PERefStack.Count > 0)
            {
                PEReference nextPERef = PERefStack.Peek();
                peRefReader = new StreamReader(new MemoryStream(CurrentEncoding.GetBytes(nextPERef.entityDecl.Value)));
            }
            else
                peRefReader = null;

            currentLine = PERef.savedCurLine;
            currentColumn = PERef.savedCurColumn;
            Position = PERef.savedPosition;
            #endregion

            storePECompilationResult(PERef);
        }
        internal void storePECompilationResult(PEReference peRef)
        {
            if (peRef.CompiledValue != null)
            {
                PEReference nextPERef = null;

                //on vérifie que l'on ne compile pas deux entités dans un même token,
                // si c'est le cas, on n'affecte aucune valeur compilée pour cette PERef
                if (PERefStack.Count > 0)
                    nextPERef = PERefStack.Peek();

                if (!(peRef.CompiledValue is AttributeDef))
                {
                    if (nextPERef != null)
                    {
                        if (nextPERef.CompiledValue == peRef.CompiledValue)
                        {
                            //clear compiled value
                            peRef.CompiledValue = null;
                            return;
                        }
                    }
                }

                //on remplace l'objet compilé dans la pile par la référence (PERef)
                List<XMLToken> tmpList = DTDObjectStack.ToList();

                int ptr = tmpList.IndexOf(peRef.CompiledValue);
                tmpList.RemoveAt(ptr);

                //si c'est une particle, on ajoute dirrectement au parent la peref et on supprime le niveau
                Particle p = peRef.CompiledValue as Particle;
                if (p != null)
                {
                    //si c'est un ContentParticle, on doit revenir au niveau précédent si il y en a un
                    Particle tmp = tmpList.ElementAt(ptr) as Particle;

                    if (tmp != null)
                    {
                        //si c'est une particule et qu'aucun enfant n'est présent, on supprime
                        if (p.Children.Count == 0)
                        {
                            //on affecte la particule suivante à la valeur compilée
                            peRef.CompiledValue = tmpList.ElementAt(ptr - 1);
                            tmpList.RemoveAt(ptr - 1);
                            tmpList.Insert(ptr - 1, peRef);
                        }
                        else
                            tmp.addChild(peRef);
                    }
                    else //normaly set peRef in the stack in place of the object
                        tmpList.Insert(ptr, peRef);
                }
                else if (peRef.CompiledValue is AttributeDef)
                {
                    List<XMLToken> fakeATTLISTToAdd = new List<XMLToken>();
                    //toute les pe précédentes dans la pile seront des attlist spéciales
                    for (int i = 0; i < PERefStack.Count; i++)
                    {
                        PEReference pi = PERefStack.ElementAt(i);
                        AttlistDecl adSup = pi.CompiledValue as AttlistDecl;
                        if (adSup == null)
                        {
                            pi.CompiledValue = new AttlistDecl { Name = pi.entityDecl.Name, isCreatedByCompiler = true };
                            fakeATTLISTToAdd.Add(pi.CompiledValue);
                        }
                        else if (adSup.Name != pi.entityDecl.Name)
                            Debugger.Break();

                    }
                    fakeATTLISTToAdd.Reverse();
                    foreach (XMLToken xt in fakeATTLISTToAdd)
                    {
                        tmpList.Insert(ptr, xt);
                    }
                    //on vérifie que l'attlist la précédant est bien une attliste spéciale pour la pe, sinon il faut la créer
                    AttlistDecl adParent = tmpList[ptr] as AttlistDecl;
                    if (adParent.Name != peRef.entityDecl.Name)
                    {
                        AttlistDecl newLevel = new AttlistDecl
                        {
                            Name = peRef.entityDecl.Name,
                            isCreatedByCompiler = true
                        };
                        newLevel.attributeDef.Add(peRef.CompiledValue);
                        peRef.CompiledValue = newLevel;
                    }
                    tmpList.Insert(ptr, peRef);
                }
                else if (peRef.CompiledValue is AttlistDecl)
                {
                    //si c'est une attlist, il faut l'ajouter au attdef de l'attlist précédente
                    AttlistDecl adParent = tmpList[ptr] as AttlistDecl;
                    if (adParent == null)
                        Debugger.Break();

                    adParent.attributeDef.Add(peRef);
                }
                else
                    tmpList.Insert(ptr, peRef);

                tmpList.Reverse();

                DTDObjectStack = new Stack<XMLToken>(tmpList);
            }
        }
        #endregion

        /// <summary>
        /// Get parameter entity reference name after % and check for ; presence
        /// </summary>
        public PEReference resolvePERef()
        {
            string tmp = "";

            if (!nextCharIsValidCharStartName)
                throw new XMLParserException("Invalid start character in ref: " + peekChar);

            tmp += nextChar;

            while (nextCharIsValidCharName && !TestNextChar(';'))
                tmp += nextChar;

            if (!TestNextChar(';', true))
                throw new XMLParserException("Invalid ref, ';' expected.");

            PEReference peRef = tmp;

            if (peRef.entityDecl.IsValueEmpty)
                return null;

            if (peRef.entityDecl.Value is ExternalID)
            {
                if (currentState == States.ExternalSubset || currentState == States.ExternalSubsetInit)
                    LoadSubset(peRef.entityDecl.Value as ExternalID);
                else
                    DTDExternalModulesID.Add(peRef.entityDecl.Value as ExternalID);
            }
            else
            {
                #region save position
                peRef.savedPosition = Position;
                peRef.savedCurLine = currentLine;
                peRef.savedCurColumn = currentColumn;
                #endregion

                PERefStack.Push(peRef);

                //le token au sommet de la pile DTDTokenStack est référencé par la valeur compilée pour la peRef,
                //il donne l'information de début de résolution de pe.
                //l'objet référencé par la valeur compilée suivra les dépilements tant que la peRef n'est pas cloturée
                peRef.CompiledValue = topOfDTDStack;

                createAdditionalLevelsIfNeeded();

                #region goto PE value location
                peRefReader = new StreamReader(new MemoryStream(CurrentEncoding.GetBytes(peRef.entityDecl.Value)));
                Position = 0;
                currentLine = peRef.entityDecl.line;
                currentColumn = peRef.entityDecl.column;
                #endregion

            }
            return peRef;
        }

        public char nextUnparsedChar
        {
            get
            {
                return (char)Read();
            }

        }
        public char nextChar
        {
            get
            {
                return (Char)Read();
            }
        }
        //TODO: advance only if true, seems bugy
        public bool TestNextChar(char c, bool advance = false, bool inhibPeRes = false)
        {
            bool res = false;

            if (inhibPeRes)
                disableParameterEntityExpansion();

            res = (peekChar == c);

            if (advance)
                Read();
            if (inhibPeRes)
                restorePreviousParameterEntityExpansionStatus();

            return res;
        }
        public void skipWhiteSpaces(bool resolvePE = true)
        {
            //save current PE resolution state
            bool PERefResolutionStatus = ParameterEntityExpansion;

            if (!resolvePE)
                //disable resolving PE
                ParameterEntityExpansion = false;

            while (!EndOfStream && char.IsWhiteSpace(peekChar))
                Read();

            //restore state
            if (!resolvePE)
                ParameterEntityExpansion = PERefResolutionStatus;

        }

        #region low level reading functions
        StreamReader peRefReader = null;
        public char peekChar
        {
            get
            {
                Char c;
                if (PERefStack.Count > 0)
                {
                    PEReference peRef = PERefStack.Peek();
                    //si la fin de l'entité est atteinte, on retourne à la position initiale
                    if (peRefReader.EndOfStream)
                    {
                        restoreLastPosition();
                        return peekChar;
                    }
                    else
                        c = (char)peRefReader.Peek();
                }
                else
                    c = (Char)base.Peek();

                if (currentState > States.prolog && currentState < States.DTDEnd && ParameterEntityExpansion)
                {
                    if (c == '%')
                    {
                        if (PERefStack.Count > 0)
                            peRefReader.Read();
                        else
                            base.Read();
                        currentPosition += CurrentEncoding.GetByteCount(new char[] { '%' });
                        PEReference peRef = resolvePERef();
                        return peekChar;
                    }

                }

                return c;

                //if (rxValidChar.IsMatch(Char.ToString((Char)Peek())))

                //else
                //    throw new DTDParserException("Invalid character");
            }
        }
        public override int Read()
        {
            if (peekChar == '\n')
            {
                currentColumn = 1;
                currentLine++;
            }
            else
                currentColumn++;

            char c;
            if (PERefStack.Count > 0)
                c = (char)peRefReader.Read();
            else
                c = (char)base.Read();
            currentPosition += CurrentEncoding.GetByteCount(new char[] { c });
            return (int)c;
        }
        public override int Read(char[] buffer, int index, int count)
        {
            int tmp;
            if (PERefStack.Count > 0)
                tmp = peRefReader.Read(buffer, index, count);
            else
                tmp = base.Read(buffer, index, count);

            foreach (Char c in buffer)
            {
                if (c == '\n')
                {
                    currentColumn = 1;
                    currentLine++;
                }
                else
                    currentColumn++;
            }

            currentPosition += CurrentEncoding.GetByteCount(buffer);
            return tmp;
        }
        #endregion

        internal string readComment()
        {
            //comments processing
            String comment = "";

            while (!EndOfStream && !comment.EndsWith("-->"))
                comment += nextUnparsedChar;

            if (comment.EndsWith("-->"))
                comment.Remove(comment.Length - 3);
            else
                throw new XMLParserException("comment closing sequence ('-->') not found.");

            return comment;
        }


        /// <summary>
        /// Fetch next dtd value that could be a PERef
        /// </summary>
        public XMLToken nextValueToken
        {
            get
            {
                //if (peekChar == '%' && currentState == states.doctype)
                //{
                //    PEReference tmp = resolvePERef;
                //    tmp.CompiledValue = nextValueToken;
                //    return tmp;
                //}
                //else
                return (XMLToken)nextWord;
            }
        }
        /// <summary>
        /// Fetch next name or PERef
        /// </summary>
        public XMLToken nextNameToken
        {
            get
            {
                //1) create token to receive the name
                XMLToken tmp = new XMLToken();
                //2) push the token on the stack
                DTDObjectStack.Push(tmp);
                //3) process whitespaces skip during which some parameter entity reference could be encounter
                skipWhiteSpaces();
                //4) normaly parse the name
                tmp.value = nextName;
                //5) process whitespaces skip to finish entity compilation if PE endPos reached, but inhib PEResolution
                //   if a new % is encounter.
                skipWhiteSpaces(false);
                //6) remove token from the stack
                //return object on the top of the DTD stack which could be a PERef or a norma Token
                return PopDTDObj();
            }
        }
        /// <summary>
        /// generic ref name parser copied from old xml parser, have to be checked, to be used for general entity
        /// </summary>
        public string nextRef
        {
            get
            {
                string tmp = "";

                if (!nextCharIsValidCharStartName)
                    throw new XMLParserException("Invalid start character in ref");

                tmp += nextChar;

                while (nextCharIsValidCharName && !TestNextChar(';'))
                    tmp += nextChar;

                if (!TestNextChar(';', true))
                    throw new XMLParserException("Invalid ref, ';' expected.");

                return tmp;
            }
        }
        /// <summary>
        /// fetch valid name
        /// </summary>
        /// <return>string</return>
        public string nextName
        {
            get
            {
                if (!nextCharIsValidCharStartName)
                    return null;

                string temp = "" + nextChar;
                while (nextCharIsValidCharName)
                    temp += nextChar;
                return temp;
            }
        }
        /// <summary>
        /// generic reading until white space or end tag when not knowing what word will follow
        /// have to be improved, should check if Parameter entity could be present
        /// </summary>
        public XMLToken nextWord
        {
            get
            {
                string temp = "";

                while (!(char.IsWhiteSpace(peekChar) || TestNextChar('>') || TestNextChar('['))) //check keyword alphabet
                    temp += nextChar;

                return new XMLToken(temp);
            }
        }
        public string nextSystemLiteral
        {
            get
            {
                char delimiter = nextChar;

                string temp = "";

                while (!TestNextChar(delimiter))
                    temp += nextChar;

                //ne doit pas arriver
                if (!TestNextChar(delimiter, true))
                    throw new XMLParserException("syntax error");

                return temp;
            }
        }
        public string nextPubidLiteral
        {
            get
            {
                char delimiter = nextChar;

                string temp = "";

                while (NextCharIsValidPubidChar && !TestNextChar(delimiter))
                    temp += nextChar;

                //ne doit pas arriver
                if (!TestNextChar(delimiter, true))
                    throw new XMLParserException("syntax error");

                return temp;
            }
        }
        /// <summary>
        /// Fetch next name or PERef
        /// </summary>
        public XMLToken nextNMTokenToken
        {
            get
            {
                //1) create token to receive the name
                XMLToken tmp = new XMLToken();
                //2) push the token on the stack
                DTDObjectStack.Push(tmp);
                //3) process whitespaces skip during which some parameter entity reference could be encounter
                skipWhiteSpaces();
                //4) normaly parse the name
                tmp.value = nextNMtoken;
                //5) process whitespaces skip to finish entity compilation if PE endPos reached, but inhib PEResolution
                //   if a new % is encounter.
                skipWhiteSpaces(false);
                //6) remove token from the stack
                //return object on the top of the DTD stack which could be a PERef or a norma Token
                return PopDTDObj();
            }
        }
        /// <summary>
        /// should check Nmtoken validity char
        /// </summary>
        public string nextNMtoken
        {
            get
            {
                string temp = "";

                while (nextCharIsValidCharName)
                    temp += nextChar;

                return temp;
            }
        }
        /// <summary>
        /// TODO: can be a PERef
        /// </summary>
        public XMLToken nextEntityValue
        {
            get
            {
                //inhib PE resolution in Entity value, resolution will be done during parsing of ELEMENT and ATTLIST
                disableParameterEntityExpansion();

                char delimiter = nextChar;
                string tmp = "";

                while (NextCharIsValidEntityValue && !TestNextChar(delimiter))
                    tmp += nextChar;

                if (!TestNextChar(delimiter, true))
                    throw new XMLParserException("Invalid entity, ending delimiter missing");

                restorePreviousParameterEntityExpansionStatus();

                return tmp;
            }
        }
        /// <summary>
        /// TODO: check delimiters presence with parameter entity
        /// 		this should return a token
        /// </summary>
        public string nextAttValue
        {
            get
            {	//added delimiters ending
                skipWhiteSpaces();

                switch (peekChar)
                {
                    case '\'':
                    case '"':
                        char delimiter = nextChar;
                        string tmp = "";

                        while (NextCharIsValidEntityValue && !TestNextChar(delimiter))
                            tmp += nextChar;

                        if (!TestNextChar(delimiter, true))
                            throw new XMLParserException("Invalid attribute value, ending delimiter missing");

                        return (AttributeOrEntityValue)tmp;
                    default:
                        XMLToken temp = nextValueToken;

                        if (!AttributeValueIsValid(temp))
                            throw new XMLParserException("Invalid attribute value: " + temp);

                        return temp;
                }

            }
        }



        internal bool nextWordIsPERef
        {
            get
            {
                disableParameterEntityExpansion();
                bool tmp = TestNextChar('%');
                restorePreviousParameterEntityExpansionStatus();
                return tmp;
            }
        }
        internal bool nextWordIsGEReference
        {
            get { return TestNextChar('&'); }
        }
        internal bool nextCharIsDelimiter
        {
            get { return TestNextChar('"') | TestNextChar('\''); }
        }

        #region Regular Expression for validity checks
        //private static Regex rxValidChar = new Regex("[\u0020-\uD7FF]");
        private static Regex rxValidChar = new Regex(@"\u0009|\u000A|\u000D|[\u0020-\uD7FF]|[\uE000-\uFFFD]");   //| [\u10000-\u10FFFF] unable to set those plans
        private static Regex rxNameStartChar = new Regex(@":|[A-Z]|_|[a-z]|[\u00C0-\u00D6]|[\u00D8-\u00F6]|[\u00F8-\u02FF]|[\u0370-\u037D]|[\u037F-\u1FFF]|[\u200C-\u200D]|[\u2070-\u218F]|[\u2C00-\u2FEF]|[\u3001-\uD7FF]|[\uF900-\uFDCF]|[\uFDF0-\uFFFD]"); // | [\u10000-\uEFFFF]
        private static Regex rxNameChar = new Regex(@":|[A-Z]|_|[a-z]|[\u00C0-\u00D6]|[\u00D8-\u00F6]|[\u00F8-\u02FF]|[\u0370-\u037D]|[\u037F-\u1FFF]|[\u200C-\u200D]|[\u2070-\u218F]|[\u2C00-\u2FEF]|[\u3001-\uD7FF]|[\uF900-\uFDCF]|[\uFDF0-\uFFFD]|-|\.|[0-9]|\u00B7|[\u0300-\u036F]|[\u203F-\u2040]");//[\u10000-\uEFFFF]|
        private static Regex rxDecimal = new Regex(@"[0-9]+");
        private static Regex rxHexadecimal = new Regex(@"[0-9a-fA-F]+");
        private static Regex rxAttributeValue = new Regex(@"[^<]");
        private static Regex rxEntityValue = new Regex(@"[^<]");
        private static Regex rxPubidChar = new Regex(@"\u0020|\u000D|\u000A|[a-zA-Z0-9]|[-\(\)\+\,\./:=\?;!\*#@\$_%]");
        #endregion

        #region Character ValidityCheck
        public bool nextCharIsValidCharStartName
        {
            get { return rxNameStartChar.IsMatch(peekChar.ToString()); }
        }
        public bool nextCharIsValidCharName
        {
            get { return rxNameChar.IsMatch(peekChar.ToString()); }
        }
        public bool NameIsValid(XMLToken name)
        {
            if (!rxNameStartChar.IsMatch(char.ConvertFromUtf32(((string)name)[0])))
                return false;

            return rxNameChar.IsMatch(name);
        }
        private bool NextCharIsValidPubidChar
        {
            get { return rxPubidChar.IsMatch(char.ConvertFromUtf32(Peek())); }
        }
        private bool AttributeValueIsValid(XMLToken name)
        {
            return string.IsNullOrEmpty(name) ? true : rxAttributeValue.IsMatch(name);
        }
        private bool NextCharIsValidEntityValue
        {
            get { return rxEntityValue.IsMatch(char.ConvertFromUtf32(Peek())); }
        }
        #endregion

        /// <summary>
        /// TODO Ajouter les réf multiples '&#__#cc#xCC#aa'
        /// </summary>
        /// <returns>Référence name without surounding ponctuation </returns>
        private GEReference readCharRef()
        {
            if (TestNextChar('x', true))    //hexadecimal value
            {
                string tmp = nextChar.ToString();
                while (!TestNextChar(';'))
                {
                    tmp += nextChar;
                    if (!rxHexadecimal.IsMatch(tmp))
                        throw new XMLParserException("Invalid hexadecimal value for char reference");
                }
                //pass the semicolon
                Read();
                return new CharRef { Value = tmp, IsHexadecimal = true };
            }
            else
            {
                string tmp = nextChar.ToString();
                while (!TestNextChar(';'))
                {
                    tmp += nextChar;
                    if (!rxDecimal.IsMatch(tmp))
                        throw new XMLParserException("Invalid decimal value for char reference");
                }
                //pass the semicolon
                Read();

                return new CharRef { Value = tmp };
            }

        }

        private void bypassCDATASection()
        {
            while (true)
            {
                if ((Char)Read() == ']')
                    if ((Char)Read() == ']')
                        if ((Char)Read() == '>')
                            break;
            }
        }
        private void bypassIgnoreSection()
        {
            while (true)
            {
                Char c = (Char)Read();
                if (c == '<')
                {
                    if ((Char)Read() == '!')
                        if ((Char)Read() == '[')
                            bypassIgnoreSection();
                }
                else
                    if (c == ']')
                        if ((Char)Read() == ']')
                            if ((Char)Read() == '>')
                                break;
            }
        }
        /// <summary>
        /// Open a new DTDReader to parse external file
        /// passing actual DTDObjects list
        /// </summary>
        /// <param name="extID"></param>
        public void LoadSubset(ExternalID extID)
        {
            //TODO: handle PUBLIC external PE
            externalFileProcessed = extID.System;

            if (!File.Exists(externalFileProcessed))
                throw new Exception("File not found: " + externalFileProcessed);


            string savedWorkingDir = Directory.GetCurrentDirectory();
            string dir = Path.GetDirectoryName(externalFileProcessed);

            if (!string.IsNullOrEmpty(dir))
                Directory.SetCurrentDirectory(dir);

            using (FileStream fs = new FileStream(externalFileProcessed, System.IO.FileMode.Open))
            {
                using (XMLParser parser = new XMLParser(fs))
                {
                    parser.XMLObjects = XMLObjects;
                    parser.parse(States.ExternalSubsetInit);
                }
            }

            Directory.SetCurrentDirectory(savedWorkingDir);
            CurrentParser = this;
            externalFileProcessed = null;
        }
    }


    public class DTDObject : XMLToken
    {
        private XMLToken _Name = "";

        public XMLToken Name
        {
            get
            {
                return _Name;
            }
            set { _Name = value; }
        }
		public string ResolvedName => _Name is PEReference ? (_Name as PEReference).entityDecl.Name.ToString() : _Name.ToString();
		/// <summary>
		/// get normalize name for csharp removing prefix and checking for accepted char in name of class
		/// and properties
		/// </summary>
		public string csName
        {
            get
            {
                //remove prefix
                string temp = Name.ToString().Split(':').Last();
                //remove '-' and convert cesure in Initial

                if (temp.Contains('-'))
                {

                    string[] res = temp.Split('-');
                    temp = res[0];

                    for (int i = 1; i < res.Length; i++)
                    {
                        temp += Char.ToUpper(res[i][0]) + res[i].Substring(1, res[i].Length - 1);
                    }
                }

                return temp;
            }
        }
        public string prefix
        {
            get { return Name.ToString().Split(':').First(); }
        }
    }
}
