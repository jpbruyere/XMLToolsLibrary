// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Linq;
using System.Text;
using System.CodeDom;
using Microsoft.CSharp;
using System.CodeDom.Compiler;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using boolean = System.Boolean;

namespace XMLTools
{
	#region Code generator Exceptions
	public class CodeGeneratorException : Exception
	{
		public CodeGeneratorException (string txt)
            : base (string.Format (".net code generator exception : {0}", txt))
		{
		}

		public CodeGeneratorException (string txt, Exception innerException)
            : base (txt, innerException)
		{
			txt = string.Format (".net code generator exception : {0}", txt);
		}
	}
	#endregion
	public class CodeGenerator
	{
		public static XMLDocument currentDTD;
		public static string strTargetFile = "generated.cs";
		public static string strBaseNameSpace = "Generated";
		public static string currentBaseType = "";
		public static string BaseType = "object";
		public static string collectionBase = "System.Collections.Generic.List";
		public static string IdBaseType = "System.string";

		#region Naming convention

		public static string InterfacePrefix = "I";
		public static string collectionPrefix = "";
		public static string collectionSuffix = "";
		public static string enumPrefix = "enum";
		public static string enumSufix = "";
		public static string fieldPrefix = "_";
		public static string fieldSufix = "";
		public static string propertyPrefix = "";
		public static string propertySufix = "Property";
		public static string childrenName = "Items";
		public static string PCDATAName = "Content";

		#endregion

		//TODO: set this as configurable
		public static string CoreElementName = "Core";
		static string currentModuleName = "";
		static string mainModuleName = "";
		static string xmlRootElement = "";
		static string targetNamespace = "";
		//found when parsing version tag in dtd
		internal static string elementBaseClass
		{ get { return ""; } }
		//{ get { return currentModuleName + "object"; } }
		static CodeCompileUnit codeBase = new CodeCompileUnit ();
		static CodeNamespace BaseNameSpace = new CodeNamespace (strBaseNameSpace);
		static CodeNamespace currentNameSpace = null;
		static Dictionary<string, string> AttributeTypePERefDetection = new Dictionary<string, string> (StringComparer.OrdinalIgnoreCase);

		static CodeGenerator ()
		{
			AttributeTypePERefDetection.Add ("boolean", "System.Boolean");
			AttributeTypePERefDetection.Add ("nonNegativeInteger", "uint");
		}

		#region Namespace helpers

		static CodeNamespace getNameSpaceFromEntityDecl (EntityDecl ed)
		{
			if (string.IsNullOrEmpty (ed.ModuleName))
				currentNameSpace = BaseNameSpace;
			else {
				string mn = strBaseNameSpace + "." + ed.ModuleName;
				currentNameSpace = getCodeNamespaceByName (mn);

				if (currentNameSpace == null) {
					currentNameSpace = new CodeNamespace (mn);
					codeBase.Namespaces.Add (currentNameSpace);
				}
			}

			return currentNameSpace;
		}

		static CodeNamespace getCodeNamespaceByName (string name)
		{
			foreach (CodeNamespace cns in codeBase.Namespaces) {
				if (cns.Name == name)
					return cns;
			}
			return null;
		}

		static void findTargetNamespace (XMLDocument dtd)
		{
			foreach (AttlistDecl ad in dtd.Attributes) {
				if (searchAttribute (ad, "xmlns", ref targetNamespace)) {
					xmlRootElement = ad.Name.ToString ();
					return;
				}
			}
		}

		#endregion

		public static void GenerateClasses (XMLDocument dtd)
		{
			currentDTD = dtd;
			//empty namespace to have using on top
			codeBase.Namespaces.Add (new CodeNamespace ());
			codeBase.Namespaces.Add (BaseNameSpace);

			//using 
			codeBase.Namespaces [0].Imports.Add (new CodeNamespaceImport ("System"));
			///if collection base is list
			codeBase.Namespaces [0].Imports.Add (new CodeNamespaceImport ("System.Collections.Generic"));

			//create base type
			currentNameSpace = BaseNameSpace;


			findTargetNamespace (dtd);

//			CodeTypeDeclaration ebc = createClass(elementBaseClass);
//			ebc.Comments.Add(new CodeCommentStatement("------ ELEMENT BASE CLASS --------"));

			//SimplifyContentSpec (dtd);

			foreach (EntityDecl ed in dtd.Entities)
				ENTITYProcessing (ed);

			foreach (ElementDecl e in dtd.Elements)
				ELEMENTProcessing (e);

			foreach (AttlistDecl ad in dtd.Attributes) {
				CodeTypeDeclaration cls = null;
				string clsName = normalizeForCSHarp (ad.Name.ToString ());
				if (!getTypeByName (clsName, ref cls)) {
					Console.WriteLine ("class not found for attlist processing:" + ad.CompiledName);
					continue;
				}

				//cls.Comments.Add (new CodeCommentStatement (" ATTLIST processing"));
                
				ATTLISTProcessing (ad, cls);

				try {
					lastCmf.EndDirectives.Add(new CodeRegionDirective (CodeRegionMode.End, "Fields"));
					lastCmp.EndDirectives.Add(new CodeRegionDirective (CodeRegionMode.End, "Properties"));
				} catch {	
				}
				lastCmf = null;
				lastCmp = null;
			}


			//foreach (EntityDecl ed in dtd.Entities)
			//    MigrateCommonAttributesToBaseClass(ed);

			GenerateCSharpCode (codeBase, strTargetFile);
		}
		static void ENTITYProcessing (EntityDecl pe)
		{
			if (pe.IsValueEmpty)
				return;

			currentNameSpace = getNameSpaceFromEntityDecl (pe);

			#region codedom variable
			CodeTypeDeclaration cls = null;
			#endregion

			Particle particle;
			ContentSpec contentSpec;
			AttlistDecl attDecl;

			AttributeTypeDeclEnumerated attTypeEnum;

			currentModuleName = pe.ModuleName;

			switch (pe.Category) {
			case EntityDecl.ParameterEntityCategories.QName:
				#region Main ELEMENTS
				string clsName = pe.ElementName;
				cls = createClass (clsName, elementBaseClass);
				cls.Comments.Add (new CodeCommentStatement ("QName Class"));
				break;
				#endregion			
			case EntityDecl.ParameterEntityCategories.Datatype:
				#region *.datatype
				if (typeIsBuiltIn (pe.ElementName))
					break;

				if (!pe.ExtractXMLObject (out attTypeEnum)) {
					//codeBase.Namespaces [0].Imports.Add (new CodeNamespaceImport (pe.ElementName + " = System.String"));
					createXMLAttributeClass(pe.ElementName);
				}
				#endregion
				break;	
			case EntityDecl.ParameterEntityCategories.Version:
				//contains public identifier of the dtd
				//ex for svg: "-//W3C//DTD SVG 1.1//EN"
				mainModuleName = pe.ModuleName;
				break;
			default:
				break;
			}

			if (pe.Category == EntityDecl.ParameterEntityCategories.Datatype)
				Debug.WriteLine ("{3,5}:{0,30}{1,20}{2,30}", pe.ElementName, pe.Category, pe.ModuleName, pe.line);

		}
		static void createXMLAttributeClass(string name)
		{
			CodeTypeDeclaration cls = createClass(name,"XMLAttribute");
			cls.Attributes = MemberAttributes.Public;

			//ctor from string
			CodeConstructor constructor = new CodeConstructor();
			constructor.Attributes = MemberAttributes.Public | MemberAttributes.Final;
			constructor.Parameters.Add(new CodeParameterDeclarationExpression(
				typeof(System.String), "str"));
			cls.Members.Add(constructor);
			//default ctor
			constructor = new CodeConstructor();
			constructor.Attributes = MemberAttributes.Public | MemberAttributes.Final;
			cls.Members.Add(constructor);

			cls.Members.AddRange(new CodeTypeMember[]{
				new CodeSnippetTypeMember("\t\tpublic static implicit operator string("+ name +" value)\n"),
				new CodeSnippetTypeMember("\t\t{\n"),
				new CodeSnippetTypeMember("\t\t\treturn value.ToString();\n"),
				new CodeSnippetTypeMember("\t\t}\n"),
			});
			cls.Members.AddRange(new CodeTypeMember[]{
				new CodeSnippetTypeMember("\t\tpublic static implicit operator "+ name +"(string value)\n"),
				new CodeSnippetTypeMember("\t\t{\n"),
				new CodeSnippetTypeMember("\t\t\treturn new " + name + "(value);\n"),
				new CodeSnippetTypeMember("\t\t}\n"),
			});

		}
		static void ELEMENTProcessing (ElementDecl e)
		{
			CodeTypeDeclaration cls = null;
			string clsName = "";
			if (e.Name is PEReference) {
				EntityDecl ed = (e.Name as PEReference).entityDecl;
				currentNameSpace = getNameSpaceFromEntityDecl (ed);
				clsName = ed.ElementName;
			}else
				clsName = normalizeForCSHarp (e.Name.ToString ());

			if (!getTypeByName (clsName, ref cls))
				cls = createClass (clsName);
			cls.IsPartial = true;
			cls.Comments.Add (new CodeCommentStatement ("Class created by ELEMENTProcessing."));
			cls.CustomAttributes.Add (new CodeAttributeDeclaration ("System.Serializable"));
			cls.CustomAttributes.Add (
				new CodeAttributeDeclaration ("System.Xml.Serialization.XmlTypeAttribute",
					new CodeAttributeArgument[] {
						new CodeAttributeArgument ("AnonymousType", new CodePrimitiveExpression (true)),
					})); 
		
			cls.CustomAttributes.Add (
				new CodeAttributeDeclaration ("System.Xml.Serialization.XmlRoot",
					new CodeAttributeArgument[] {
						new CodeAttributeArgument ("IsNullable", new CodePrimitiveExpression (false)),
						new CodeAttributeArgument ("ElementName", new CodePrimitiveExpression (e.Name.ToString ())),
					}));

			ContentSpecProcessing (e.contentSpec, cls);
		}


		/// <summary>
		/// simple processing for testing
		/// </summary>
		static void ContentSpecProcessing (XMLToken contentSpec, CodeTypeDeclaration cls)
		{
			ContentSpec cs = contentSpec.Extract<ContentSpec> ();
			//ContentSpec cs = contentSpec as ContentSpec;

			switch (cs.contentType) {
			case ContentTypes.Any:
				//CodeMemberProperty p = createListMember (cls, childrenName, elementBaseClass);
				//p.CustomAttributes.Add (new CodeAttributeDeclaration ("System.Xml.Serialization.XmlAnyElement"));
				break;
			case ContentTypes.Children:
				ContentParticleProcessing2 (contentSpec, cls);
				break;
			case ContentTypes.Mixed:
				CreatePCDATAMembers (cls);
				ContentParticleProcessing2 (contentSpec, cls);
				break;
			case ContentTypes.Empty:
				break;
			}				
		}
		static void ContentParticleProcessing2 (XMLToken _particle, CodeTypeDeclaration cls)
		{		
			CodeMemberProperty p = createListMember (cls, childrenName, elementBaseClass);

			ContentParticleChildrenProcessing2 (_particle, p);
		}
		static void ContentParticleChildrenProcessing2 (XMLToken _particle, CodeMemberProperty p)
		{
			if (_particle == null) {
				Debug.WriteLine ("ContentParticleProcessing: null particle for " + p.Name);
				return;
			}

			Particle particle = _particle.Extract<Particle> ();

			if (particle == null) {
				ParticleBase pb = _particle.Extract<ParticleBase> ();

				p.CustomAttributes.Add (
					new CodeAttributeDeclaration ("System.Xml.Serialization.XmlElementAttribute",
						new CodeAttributeArgument[] {
							new CodeAttributeArgument (new CodePrimitiveExpression (pb.Name.ToString ())),
							new CodeAttributeArgument (new CodeTypeOfExpression (normalizeForCSHarp (pb.Name)))
						}));
			} else {
				#region create mother class based on entity category
				if (_particle is PEReference) {
					EntityDecl ed = (_particle as PEReference).entityDecl;
					if (ed.Category == EntityDecl.ParameterEntityCategories.Class) {
						CodeTypeDeclaration ctd = null;
						string clsName = ed.ElementName;
						if (!getTypeByName (clsName, ref ctd)) {
							ctd = createClass (clsName);
							ctd.IsPartial = true;
							ctd.Comments.Add(new CodeCommentStatement("base class created with Entity name",true));
							foreach (XMLToken tk in particle.Children) {
								CodeTypeDeclaration derivedClass = null;
								if (tk is PEReference) {
									ed = (tk as PEReference).entityDecl;
									if (!getTypeByName (ed.ElementName,ref derivedClass)) {
										Debug.WriteLine ("\n\tClass not yet defined\n" + p.Name);
										continue;
									}
								} else {
									if (!getTypeByName (normalizeForCSHarp((tk as ParticleBase).CompiledName),ref derivedClass)) {
										Debug.WriteLine ("\n\tClass not yet defined\n" + p.Name);
										continue;
									}
								}
								if (derivedClass.Name != clsName)
									derivedClass.BaseTypes.Add (clsName);
							}
							return;
						}
					}
				}
				#endregion
				foreach (XMLToken tk in particle.Children)
					ContentParticleChildrenProcessing2 (tk, p);
			}
		}

		static int XmlTypeAttributeIndex = 1;
		static int XmlRootAttributeIndex = 2;

		//CodeRegionDirective regFields = null;

//		static void ATTLISTProcessing (XMLToken t, CodeTypeDeclaration cls, bool eventsMembers = false, CodeTypeDeclaration iFace = null)
//		{
//
//			AttlistDecl ald = t.Extract<AttlistDecl> ();
//			ATTLISTProcessing (ald, cls, eventsMembers, iFace, addInterfaceMembers);
//		}
		static void ATTLISTProcessing (AttlistDecl ald, CodeTypeDeclaration cls, CodeTypeDeclaration currentInterface = null, bool eventsMembers = false)
		{
			//bool isNewInterface = false;
			//CodeTypeDeclaration ctdNewInterface = null;
			string clsName = ald.CompiledName;

			if (clsName.EndsWith ("events", true, CultureInfo.InvariantCulture))
				eventsMembers = true;
				
			if (ald.attributeDef.Count == 0) 
				return;

			if (ald.attributeDef.Count > 1 && ald.isCreatedByCompiler) {
				CodeTypeDeclaration ctdInterface = null;
				//create interface
				string InterfaceName = 
					InterfacePrefix +
					clsName;
					
				if (currentInterface == null)
					cls.BaseTypes.Add (InterfaceName);
				else if (ctdInterface == null)
					currentInterface.BaseTypes.Add (InterfaceName);

				getTypeByName (InterfaceName, ref ctdInterface);

				//si l'interface n'existe pas encore
				if (ctdInterface == null) 
					currentInterface = createInterface (InterfaceName);
					//	isNewInterface = true;
				
			} 

			foreach (XMLToken item in ald.attributeDef) {
				AttlistDecl alDecl = item.Extract<AttlistDecl> ();

				if (alDecl == null) {
					//Attdef processing
					if (eventsMembers)
						ATTDEFEventsProcessing (item, cls, currentInterface);
					else
						ATTDEFProcessing (item, cls, currentInterface);
				} else
					ATTLISTProcessing (alDecl, cls, currentInterface, eventsMembers);
			}


				//si c'est une attlist dans une autre attlist, c'est forcément via une PEREference
//				PEReference peRef = t as PEReference;
//				//tweek to avoid xmlns
//				if (string.Compare (aldElementName, "xmlns", true) == 0){
//					ATTLISTProcessing (ald, cls, eventsMembers);
//					continue;
//				}


				//Stack<CodeRegionDirective> regions = new Stack<CodeRegionDirective> ();


		}

		static CodeMemberField lastCmf = null;
		static CodeMemberProperty lastCmp = null;

		static void ATTDEFEventsProcessing (XMLToken t, CodeTypeDeclaration cls, CodeTypeDeclaration iFace=null)
		{
			AttributeDef a = t.Extract<AttributeDef> ();
			if (a == null)
				Debugger.Break ();
//
//			DefaultDecl dd = a.defaultDecl.Extract<DefaultDecl> ();
//			AttributeTypeDeclEnumerated atde = a.attributeTypeDecl.Extract<AttributeTypeDeclEnumerated> ();
//
//			//si c'est une énumération, alors c'est un membre normal
//			if (atde != null) {
//				ATTDEFProcessing (t, cls,iFace);
//				return;
//			}
//

			if (iFace != null)
				createEvent (a.CompiledName, iFace);

			createEvent(a.CompiledName,cls).Attributes = MemberAttributes.Public;
		}

		static CodeMemberEvent createEvent(string name, CodeTypeDeclaration ctd)
		{
			CodeMemberEvent evt = new CodeMemberEvent();
			evt.Name = normalizeForCSHarp(name);
			evt.Type = new CodeTypeReference("System.EventHandler");
			evt.Attributes = MemberAttributes.Final;
			ctd.Members.Add (evt);
			return evt;
		}

		static void ATTDEFProcessing (XMLToken t, CodeTypeDeclaration cls, CodeTypeDeclaration iFace=null)
		{
			AttributeDef a = t.Extract<AttributeDef> ();
			if (a == null)
				Debugger.Break ();

			DefaultDecl dd = a.defaultDecl.Extract<DefaultDecl> ();

			//process namespace attribute
			string[] xns = a.Name.ToString ().Split (new char[] { ':' });
			switch (xns [0]) {
			case "xmlns":
				if (xns.Length == 1) {
					cls.CustomAttributes [XmlRootAttributeIndex].Arguments.Add (
						new CodeAttributeArgument ("Namespace", new CodePrimitiveExpression (dd.DefaultValue.ToString ())));
					cls.CustomAttributes [XmlTypeAttributeIndex].Arguments.Add (
						new CodeAttributeArgument ("Namespace", new CodePrimitiveExpression (dd.DefaultValue.ToString ())));
					return;
				} else {
					Debug.WriteLine ("TODO: " + a.ToString ());
					return;
				}
				break;
			case "xml":
				Debug.WriteLine ("TODO: " + a.ToString ());
				return;
				break;
			case "xlink":
				Debug.WriteLine ("TODO: " + a.ToString ());
				return;
				break;
			default:
				if (xns.Length > 1) {
					Debug.WriteLine ("TODO: " + a.ToString ());
					return;
				}
				break;
			}
				
//			if (a.CompiledName == "onmousedown")
//				Debugger.Break ();
			CodeMemberField cmf = new CodeMemberField ();
			CodeMemberProperty cmp = new CodeMemberProperty ();

			//cmf.Comments.Add (new CodeCommentStatement ("ATTLISTProcessing: " + a.ToString ()));
			cmf.Attributes = MemberAttributes.Final;

			cmp.Comments.Add (new CodeCommentStatement ("ATTLISTProcessing: " + t.ToString ()));
			cmp.Attributes = MemberAttributes.Public | MemberAttributes.Final;

			string typeName = "";
			string enumName = "";

			if (a.attributeTypeDecl is PEReference) {
				typeName = (a.attributeTypeDecl as PEReference).entityDecl.ElementName;
				enumName = enumPrefix + normalizeForCSHarp (typeName) + enumSufix;
			} else {
				enumName = enumPrefix + normalizeForCSHarp (a.CompiledName) + enumSufix;
				typeName = "System.string";
			}

			string attName = propertyPrefix + normalizeForCSHarp (a.CompiledName) + propertySufix;
			string fieldName = fieldPrefix + normalizeForCSHarp (a.CompiledName) + fieldSufix;

			if (a.Name.ToString () != attName) {
				cmp.CustomAttributes.Add (
					new CodeAttributeDeclaration ("System.Xml.Serialization.XmlAttributeAttribute",                            
						new CodeAttributeArgument (new CodePrimitiveExpression (a.Name.ToString ()))));				
			}

			cmp.Name = attName;
			cmp.HasGet = true;
			cmp.HasSet = true;

			cmf.Name = fieldName;

			AttributeTypeDeclEnumerated atde = a.attributeTypeDecl.Extract<AttributeTypeDeclEnumerated> ();
			if (atde != null) {
				//enum
				//si ca référence une notation, il faudrait dériver les class des notations
				//d'une classe générique qui servirait de type pour cet attribut
				CodeTypeDeclaration ctdEnum = null;
				if (!(getEnumDeclaration (enumName, ref ctdEnum) || getEnumDeclaration (enumName, ref ctdEnum, cls))) {
					ctdEnum = createEnumeration (enumName, atde.tokenList);
					if (a.attributeTypeDecl is PEReference || t is PEReference || iFace != null)
						currentNameSpace.Types.Add (ctdEnum);
					else
						cls.Members.Add (ctdEnum);
				}

				cmp.Type = new CodeTypeReference (enumName);
				cmf.Type = new CodeTypeReference (enumName);

				if (dd.type != DefaultDeclTypes.IMPLIED &&
					dd.type != DefaultDeclTypes.REQUIRED) {
					cmp.CustomAttributes.Add (
						new CodeAttributeDeclaration ("System.ComponentModel.DefaultValue",  
							new CodeAttributeArgument (
								new CodeFieldReferenceExpression (
									new CodeTypeReferenceExpression (enumName), 										
									normalizeForCSHarp (dd.DefaultValue.ToString ())))));
					cmf.InitExpression =
						new CodeFieldReferenceExpression (
							new CodeTypeReferenceExpression (enumName), 										
							normalizeForCSHarp (dd.DefaultValue.ToString ()));
				}
			} else { 

				AttributeTypeDeclTokenized atdt = a.attributeTypeDecl.Extract<AttributeTypeDeclTokenized> ();
				if (atdt != null) {
					//cmf.Comments.Add(new CodeCommentStatement("AttributeType is Tokenized"));
					switch (atdt.type) {
					case AttributeTypeDeclTokenized.TokenizedTypes.ID:
						cmf.Type = cmp.Type = new CodeTypeReference (IdBaseType);
						break;
					case AttributeTypeDeclTokenized.TokenizedTypes.IDREF:
						cmf.Type = cmp.Type = new CodeTypeReference (IdBaseType);
						break;
					case AttributeTypeDeclTokenized.TokenizedTypes.ENTITY:
						cmf.Type = cmp.Type = new CodeTypeReference (BaseType);
						break;
					case AttributeTypeDeclTokenized.TokenizedTypes.ENTITIES:
						CodeTypeReference ctr = new CodeTypeReference ("List");
						ctr.TypeArguments.Add (elementBaseClass);
						cmp.Type = ctr;
						cmp.Name = normalizeForCSHarp (a.Name.ToString (), true);
						break;
					case AttributeTypeDeclTokenized.TokenizedTypes.NMTOKEN:
						cmf.Type = cmp.Type = new CodeTypeReference ("System.String");
						break;
					case AttributeTypeDeclTokenized.TokenizedTypes.NMTOKENS:
						CodeTypeReference ctrTKS = new CodeTypeReference ("List");
						ctrTKS.TypeArguments.Add ("System.String");
						cmp.Type = ctrTKS;
						cmp.Name = normalizeForCSHarp (a.Name.ToString (), true);
						break;
					}
				}else{
					cmp.Type = new CodeTypeReference (typeName);
					cmf.Type = new CodeTypeReference (typeName);

					switch (dd.type) {
					case DefaultDeclTypes.NotSet:
						cmp.CustomAttributes.Add (
							new CodeAttributeDeclaration ("System.ComponentModel.DefaultValue",  
								new CodeAttributeArgument (
									new CodePrimitiveExpression (dd.DefaultValue.ToString ()))));
						cmf.InitExpression =
							new CodePrimitiveExpression (dd.DefaultValue.ToString ());
						break;
					case DefaultDeclTypes.REQUIRED:
						break;
					case DefaultDeclTypes.IMPLIED:
						cmp.CustomAttributes.Add (
							new CodeAttributeDeclaration ("System.ComponentModel.DefaultValue",  
								new CodeAttributeArgument (
									new CodePrimitiveExpression (""))));
						break;
					case DefaultDeclTypes.FIXED:
						//cmp.Attributes |= MemberAttributes.Const;
						//cmp.InitExpression = new CodePrimitiveExpression(a.defaultDecl.Extract<DefaultDecl>().DefaultValue.ToString());
						break;
					default:
						break;
					}
				} 
			}

			if (iFace != null) {
				iFace.Members.Add (cmp);
			}

			CreateGetterAndSetter (cmp, fieldName);

//			if (attName == "OnfocusinProperty")
//				Debugger.Break ();
			if (lastCmf == null) {
				cmf.StartDirectives.Add(new CodeRegionDirective (CodeRegionMode.Start, "Fields"));
				cmp.StartDirectives.Add(new CodeRegionDirective (CodeRegionMode.Start, "Properties"));
			}
			cls.Members.Add (cmf);
			cls.Members.Add (cmp);

			lastCmf = cmf;
			lastCmp = cmp;
		}

		static CodeMemberProperty CreatePCDATAMembers (CodeTypeDeclaration cls)
		{
			string field = fieldPrefix + PCDATAName + fieldSufix;

			CodeMemberField cmf = new CodeMemberField () {
				Name = field,
				Type = new CodeTypeReference (typeof(string)),
				Attributes = MemberAttributes.Private
			};

			CodeMemberProperty cmp = CreateFullProperty (field, PCDATAName, typeof(string));


			cmp.CustomAttributes.Add (
				new CodeAttributeDeclaration ("System.Xml.Serialization.XmlTextAttribute"));

			cls.Members.Add (cmf);
			cls.Members.Add (cmp);

			return cmp;
		}

		static CodeMemberProperty createListMember (CodeTypeDeclaration cls, string name, string itemsType = "")
		{
			string field = fieldPrefix + name + fieldSufix;

			if (string.IsNullOrEmpty (itemsType))
				itemsType = BaseType;

			CodeMemberField f = new CodeMemberField ("List<" + itemsType + ">", field);
			f.InitExpression =
				new CodeObjectCreateExpression ("List<" + itemsType + ">", new CodeExpression[] { });
			f.Attributes = MemberAttributes.Private;

			CodeMemberProperty p = CreateFullProperty (field, name, "List<" + itemsType + ">");

			cls.Members.Add (f);
			cls.Members.Add (p);

			return p;
		}

		static CodeMemberProperty CreateFullProperty (string field, string name, string type)
		{
			CodeMemberProperty property = new CodeMemberProperty () {
				Name = name,
				Type = new CodeTypeReference (type),
				Attributes = MemberAttributes.Public
			};

			CreateGetterAndSetter (property, field);

			return property;
		}

		static CodeMemberProperty CreateFullProperty (string field, string name, Type type)
		{
			CodeMemberProperty property = new CodeMemberProperty () {
				Name = name,
				Type = new CodeTypeReference (type),
				Attributes = MemberAttributes.Public
			};

			CreateGetterAndSetter (property, field);

			return property;
		}

		static void CreateGetterAndSetter (CodeMemberProperty property, string field)
		{
			property.SetStatements.Add (
				new CodeAssignStatement (
					new CodeFieldReferenceExpression (null, field),
					new CodePropertySetValueReferenceExpression ()));

			property.GetStatements.Add (
				new CodeMethodReturnStatement (
					new CodeFieldReferenceExpression (null, field)));
		}

		static bool searchAttribute (AttlistDecl ad, string name, ref string result)
		{
			foreach (XMLToken t in ad.attributeDef) {
				if (t is PEReference) {
					if (searchAttribute ((t as PEReference).CompiledValue as AttlistDecl, name, ref result))
						return true;
				} else {
					AttributeDef adef = t as AttributeDef;
					if (adef.Name.ToString () == name) {
						result = adef.defaultDecl.Extract<DefaultDecl> ().DefaultValue.ToString ();
						return true;
					}
				}
			}
			result = "";
			return false;
		}


		static void ContentParticleProcessing (XMLToken _particle, CodeTypeDeclaration cls)
		{
			if (_particle == null) {
				Debug.WriteLine ("ContentParticleProcessing: null particle for " + cls.Name);
				return;
			}
			Particle particle = _particle.Extract<Particle> ();

			if (particle == null) {
				Debug.WriteLine ("ContentParticleProcessing: null particle for " + cls.Name);
				return;
			}

			switch (particle.ParticleType) {
			case ParticleTypes.Unknown:
				//if unknown sans doute n'y a t-il qu'un seul élément
				if (particle.Children.Count > 1)
					Debugger.Break ();
				if (particle.isContentSpecRoot) {
					CodeMemberProperty p = createListMember (cls, childrenName, elementBaseClass);
					foreach (XMLToken tk in particle.Children) {
						p.CustomAttributes.Add (
							new CodeAttributeDeclaration ("System.Xml.Serialization.XmlElementAttribute",
								new CodeAttributeArgument[] {
									new CodeAttributeArgument (new CodePrimitiveExpression (tk.CompiledName)),
									new CodeAttributeArgument (new CodeSnippetExpression ("typeof(" + normalizeForCSHarp (tk.CompiledName) + ")"))
								}));								 
					}

				} else {
					if (particle.Children.Count == 1) {
						if (particle.Children [0] is PEReference) {
							//PEReference peRef = particle.Children[0] as PEReference;
							//createMember(cls, peRef.entityDecl.ElementName, peRef.entityDecl.ElementName);
						}
					}
					//Debugger.Break();
				}
				break;
			case ParticleTypes.Choice:
				if (particle.IsUnique) {
					List<List<string>> names = new List<List<string>> ();
					foreach (XMLToken tk in particle.Children)
						names.Add (new List<string> (splipOnCasing (tk.CompiledName)));
					createMember (cls, createNameFromListOfListNames (names));
				} else
					createListMember (cls, childrenName, elementBaseClass);
				break;
			case ParticleTypes.Sequence:
				foreach (XMLToken t in particle.Children) {
					if (t is PEReference) {
						PEReference peRef = t as PEReference;
						string name = "unamed";
						if (peRef.CompiledValue is DTDObject)
							name = (peRef.CompiledValue as DTDObject).CompiledName;
						else
							name = peRef.entityDecl.CompiledName;
						createMember (cls, name, peRef.entityDecl.CompiledName);
					} else {
						Particle p = t as Particle;
						ContentParticleProcessing (p, cls);
					}

				}
				break;
			default:
				break;
			}
		}



	
		static void attributeTypeProcessing (AttributeTypeDecl atd)
		{

		}

		#region not used

		static void createElementClass (string className, string xmlName)
		{
			CodeTypeDeclaration cls = null;

			if (getTypeByName (className, ref cls))
				return;

			cls = createClass (className, elementBaseClass);
			cls.CustomAttributes.Add (new CodeAttributeDeclaration ("System.Serializable"));
			cls.CustomAttributes.Add (
				new CodeAttributeDeclaration ("System.Xml.Serialization.XmlTypeAttribute",
					new CodeAttributeArgument[] {
						new CodeAttributeArgument ("AnonymousType", new CodePrimitiveExpression (true)),
						new CodeAttributeArgument ("Namespace", new CodePrimitiveExpression ("test"))
					}));        
		}

		#endregion

		static void createMember (CodeTypeDeclaration cls, string name, string type = "")
		{
			if (string.IsNullOrEmpty (type))
				type = BaseType;

			CodeMemberField member = new CodeMemberField {
				Type = new CodeTypeReference (type),
				Name = name,
				Attributes = MemberAttributes.Public
			};

			cls.Members.Add (member);
		}

		static CodeTypeDeclaration createEnumeration (string name, List<XMLToken> list)
		{

			if (typeAlreadyDeclared (name)) {
				Console.WriteLine ("Enum already declared: {0}", name);
				return null;
			}

			CodeTypeDeclaration enumClass = new CodeTypeDeclaration {
				Name = name,
				IsEnum = true
			};
			enumClass.CustomAttributes.Add (
				new CodeAttributeDeclaration ("System.FlagsAttribute"));        



			foreach (XMLToken tk in list) {
				CodeMemberField e = new CodeMemberField (name, normalizeForCSHarp (tk.ToString ()));
				e.CustomAttributes.Add (
					new CodeAttributeDeclaration ("System.Xml.Serialization.XmlEnumAttribute",
						new CodeAttributeArgument (new CodePrimitiveExpression (tk.ToString ()))));  
				enumClass.Members.Add (e);
			}
				

			return enumClass;
		}

		static bool getEnumDeclaration (string name, ref CodeTypeDeclaration enumDecl, CodeTypeDeclaration cls = null)
		{
			if (cls == null) {
				foreach (CodeTypeDeclaration t in currentNameSpace.Types) {
					if (t.Name == name) {
						enumDecl = t;
						return true;
					}
				}
			} else {
				foreach (CodeTypeMember t in cls.Members) {
					if (t.Name == name) {
						enumDecl = t as CodeTypeDeclaration;
						return true;
					}
				}
			}

			return false;
		}

		static bool typeAlreadyDeclared (string name)
		{
			foreach (CodeTypeDeclaration t in currentNameSpace.Types)
				if (t.Name == name)
					return true;

			return false;
		}

		/// <summary>
		/// Find type by name
		/// </summary>
		/// <returns><c>true</c>, if type is already defined, <c>false</c> otherwise.</returns>
		/// <param name="name">Name as defined in the dtd</param>
		/// <param name="result">CodeTypeDeclaration if exists</param>
		static bool getTypeByName (string name, ref CodeTypeDeclaration result)
		{
			foreach (CodeTypeDeclaration t in currentNameSpace.Types) {
				if (t.Name == name) {
					result = t;
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Creates a new class
		/// </summary>
		/// <returns>CodeTypeDeclaration</returns>
		/// <param name="name">Name as in the DTD</param>
		/// <param name="baseClass">custom Base class, could be null</param>
		static CodeTypeDeclaration createClass (string name, string baseClass = null)
		{
			if (typeAlreadyDeclared (name)) {
				Console.WriteLine ("class already declared {0}", name);
				return null;
			}

			CodeTypeDeclaration newClass = new CodeTypeDeclaration (name);
			newClass.IsClass = true;

			if (string.IsNullOrEmpty (baseClass)) {
				if (!string.IsNullOrEmpty (currentBaseType))
					newClass.BaseTypes.Add (currentBaseType);
				else if (!(string.IsNullOrEmpty (BaseType) || BaseType == "object"))
					newClass.BaseTypes.Add (BaseType);
			} else
				newClass.BaseTypes.Add (baseClass);

			currentNameSpace.Types.Add (newClass);
			return newClass;
		}

		static CodeTypeDeclaration createInterface (string name)
		{		

			if (typeAlreadyDeclared (name))
				Console.WriteLine ("class already declared {0}", name);

			CodeTypeDeclaration newClass = new CodeTypeDeclaration (name);
			newClass.IsInterface = true;

			currentNameSpace.Types.Add (newClass);
			return newClass;
		}

		static bool typeIsBuiltIn (string typeName)
		{
			Assembly a = Assembly.GetAssembly (typeof(bool));
			return a.GetExportedTypes ().Any (p => string.Compare (p.Name, typeName, true) == 0);

		}

		#region simplification of content model

		static void simplifyParticule (Particle p)
		{
			if (p == null)
				return;

			if (p.Children.Count == 1) {
				Particle c = p.Children [0] as Particle;

				if (c == null) {
					//PEReference pe = p.Children[0] as PEReference;
					//if (pe == null)
					//    return;

					//int i = p.Parent.Children.IndexOf(p);
					//p.Parent.Children.RemoveAt(i);
					//p.Parent.Children.Insert(i, c);
					return;
				}

				if (p.ParticleType == ParticleTypes.Unknown) {
					p.IsUnique = p.IsUnique & c.IsUnique;
					p.IsOptional = p.IsOptional | c.IsOptional;
					p.Name = c.Name;
					p.Children = c.Children;
				}
				simplifyParticule (p);
			} else {
				for (int i = 0; i < p.Children.Count; i++)
					simplifyParticule (p.Children [i] as Particle);
			}
		}

		static void SimplifyContentSpec (XMLDocument dtd)
		{
			foreach (EntityDecl ed in dtd.Entities)
				simplifyParticule (ed.CompiledValue as Particle);
		}

		#endregion

		static void GenerateCSharpCode (CodeCompileUnit codeBase, string file)
		{
			CodeDomProvider codeDomProvider = new CSharpCodeProvider ();

			//On définit les options de génération de code
			CodeGeneratorOptions options = new CodeGeneratorOptions ();
			//On demande a ce que le code généré soit dans le même ordre que le code inséré
			options.VerbatimOrder = false;
			//options.BracingStyle = "C";
			//options.BracingStyle = "C";
			options.ElseOnClosing = true;
			options.BlankLinesBetweenMembers = false;

			using (IndentedTextWriter itw = new IndentedTextWriter (new StreamWriter (file, false), "\t")) {
				//On demande la génération proprement dite
				codeDomProvider.GenerateCodeFromCompileUnit (codeBase, itw, options);
				itw.Flush ();
			}
			Console.WriteLine ("C# code generated: " + file);
		}

		#region string helpers for c# naming

		static string createNameFromListOfListNames (List<List<string>> strings)
		{
			string tmp = createPrefixFromListOfListOfNames (strings) + createSuffixFromListOfListOfNames (strings);
			return string.IsNullOrEmpty (tmp) ? "Child" : tmp;
		}

		static string createPrefixFromListOfListOfNames (List<List<string>> strings)
		{
			string prefix = "";

			int minimalSubStringCount = int.MaxValue;
			foreach (List<string> ss in strings) {
				if (ss.Count < minimalSubStringCount)
					minimalSubStringCount = ss.Count;
			}

			for (int j = 0; j < minimalSubStringCount; j++) {
				string currentString = strings [0] [j];
				for (int i = 1; i < strings.Count; i++) {
					if (strings [i] [j] != currentString)
						return prefix;
				}
				prefix += currentString;
			}
			return string.IsNullOrEmpty (prefix) ? "Child" : prefix;
		}

		static string createSuffixFromListOfListOfNames (List<List<string>> strings)
		{
			string suffix = "";

			int minimalSubStringCount = int.MaxValue;
			foreach (List<string> ss in strings) {
				if (ss.Count < minimalSubStringCount)
					minimalSubStringCount = ss.Count;
			}

			for (int j = 0; j < minimalSubStringCount; j++) {
				string currentString = strings [0] [strings [0].Count - j - 1];
				for (int i = 1; i < strings.Count; i++) {
					if (strings [i] [strings [i].Count - j - 1] != currentString)
						return suffix;
				}
				suffix += currentString;
			}
			return string.IsNullOrEmpty (suffix) ? "Child" : suffix;
		}

		static string createNameFromListOfNames (List<string> strings)
		{
			string tmp = createPrefixFromListOfNames (strings) + createSufixFromListOfNames (strings);
			return string.IsNullOrEmpty (tmp) ? "Child" : tmp;
		}

		static string createSufixFromListOfNames (List<string> strings)
		{
			int minimalLength = int.MaxValue;
			foreach (string s in strings) {
				if (s.Length < minimalLength)
					minimalLength = s.Length;
			}
			for (int i = 0; i < minimalLength; i++) {
				char c = strings [0] [strings [0].Length - i - 1];
				for (int j = 1; j < strings.Count; j++) {
					if (strings [j] [strings [j].Length - i - 1] != c)
						return i == 0 ? null : strings [0].Substring (strings [0].Length - i);
				}
			}

			return strings [0].Substring (0, strings [0].Length - minimalLength);
		}

		static string createPrefixFromListOfNames (List<string> strings)
		{
			int minimalLength = int.MaxValue;
			foreach (string s in strings) {
				if (s.Length < minimalLength)
					minimalLength = s.Length;
			}
			for (int i = 0; i < minimalLength; i++) {
				char c = strings [0] [i];
				for (int j = 1; j < strings.Count; j++) {
					if (strings [j] [i] != c)
						return i == 0 ? null : strings [0].Substring (0, i);
				}
			}

			return strings [0].Substring (0, minimalLength);
		}

		static string[] splipOnCasing (string s)
		{
			if (string.IsNullOrEmpty (s))
				return null;

			bool currentIsUpper = false;
			List<string> result = new List<string> ();
			string tmp = new string (new char[] { s [0] });

			if (char.IsUpper (s [0]))
				currentIsUpper = true;

			for (int i = 1; i < s.Length; i++) {
				if ((char.IsUpper (s [i]) ^ currentIsUpper) & char.IsUpper (s [i])) {
					result.Add (tmp);
					tmp = new string (new char[] { s [i] });
				} else
					tmp += new string (new char[] { s [i] });
				currentIsUpper = char.IsUpper (s [i]);
			}
			result.Add (tmp);
			return result.ToArray ();
		}

		public static string normalizeForCSHarp (string name, bool takeLastPartOnly = false)
		{
			string temp = "";

			if (string.IsNullOrEmpty (name))
				return "ERROR";

			if (Char.IsDigit (name [0]))
				temp = "v";


			string[] resTmp = name.Split (new char[] { '-' });
			List<string> res = new List<string> ();

			foreach (string s in resTmp) {
				string[] casingSplit = splipOnCasing (s);
				res.AddRange (casingSplit);
			}

			if (takeLastPartOnly)
				temp += char.ToUpper (res [res.Count - 1] [0]) + res [res.Count - 1].Substring (1);
			else
				for (int i = 0; i < res.Count; i++)
					temp += char.ToUpper (res [i] [0]) + res [i].Substring (1).ToLower ();


			return temp;
		}

		#endregion

		/// <summary>
		/// 
		/// </summary>
		/// <remarks>
		/// this mechanism make it difficult apriori to use XML built in feature in c#
		/// </remarks>
		/// <param name="ed"></param>
		static void MigrateCommonAttributesToBaseClass (EntityDecl ed)
		{
			//only evaluate class entities, only them trigger class derivation
			if ((ed.Category != EntityDecl.ParameterEntityCategories.Class) | ed.IsValueEmpty)
				return;

			currentNameSpace = getNameSpaceFromEntityDecl (ed);

			CodeTypeDeclaration baseClass = null;

			if (!getTypeByName (ed.ElementName, ref baseClass))
				throw new CodeGeneratorException ("Base Class not found during migration of attribute:" + ed.ElementName);

			//get all derived class
			List<CodeTypeDeclaration> types = new List<CodeTypeDeclaration> ();
			CodeTypeReference baseClassRef = new CodeTypeReference (ed.ElementName);
			foreach (CodeTypeDeclaration ctd in currentNameSpace.Types) {
				if (ctd.Name == ed.ElementName)
					continue;
				if (ctd.BaseTypes.Count > 0)
				if (ctd.BaseTypes [0].BaseType == baseClassRef.BaseType)
					types.Add (ctd);
			}

			List<CodeTypeMember> commonMembers = new List<CodeTypeMember> ();
			//test attributes common in all class
			foreach (CodeTypeMember m0 in types[0].Members) {
				bool memberIsInAllDerivedClass = true;
				//search for m0 in alltype
				for (int i = 1; i < types.Count; i++) {
					bool memberIsPresent = false;
					//search for m0 in type
					foreach (CodeTypeMember m in types[i].Members) {
						if (m.GetHashCode () == m0.GetHashCode ()) {
							memberIsPresent = true;
							break;
						}
					}
					if (!memberIsPresent) {
						memberIsInAllDerivedClass = false;
						break;
					}
				}
				if (memberIsInAllDerivedClass)
					commonMembers.Add (m0);
			}

			//remove common attributes from derived classes
			foreach (CodeTypeDeclaration t in types) {
				foreach (CodeTypeMember m in commonMembers) {
					CodeTypeMember[] memberList = new CodeTypeMember[t.Members.Count];
					t.Members.CopyTo (memberList, 0);
					foreach (CodeTypeMember mm in memberList) {
						if (mm.GetHashCode () == m.GetHashCode ())
							t.Members.Remove (mm);
					}
				}
			}
			//add common members to base class
			foreach (CodeTypeMember m in commonMembers) {
				m.Comments.Add (new CodeCommentStatement ("Member imported from derived classes"));
				baseClass.Members.Add (m);
			}
		}
		/*
        //TODO
        static void createClassTree(XMLDocument dtd)
        {
            foreach (EntityDecl pe in dtd.Entities)
            {
                currentNameSpace = getNameSpaceFromEntityDecl(pe);

                #region codedom variable
                CodeTypeDeclaration cls = null;
                #endregion

                Particle particle;
                ContentSpec contentSpec;
                AttlistDecl attDecl;

                AttributeTypeDeclEnumerated attTypeEnum;

                currentModuleName = pe.ModuleName;

                switch (pe.Category)
                {
                    case EntityDecl.ParameterEntityCategories.Undefined:
                        break;
                    case EntityDecl.ParameterEntityCategories.Mod:
                        break;
                    case EntityDecl.ParameterEntityCategories.Module:
                        break;
                    case EntityDecl.ParameterEntityCategories.QName:
                        break;
                    case EntityDecl.ParameterEntityCategories.Content:
                        break;
                    case EntityDecl.ParameterEntityCategories.Class:
                        break;
                    case EntityDecl.ParameterEntityCategories.Mix:
                        break;
                    case EntityDecl.ParameterEntityCategories.Attrib:
                        break;
                    case EntityDecl.ParameterEntityCategories.Datatype:
                        break;
                    case EntityDecl.ParameterEntityCategories.Attlist:
                        //INCLUDE or IGNORE
                        break;
                    case EntityDecl.ParameterEntityCategories.Element:
                        //INCLUDE or IGNORE
                        break;
                    case EntityDecl.ParameterEntityCategories.xmlns:
                        break;
                    default:
                        break;
                }
                if (pe.Category == EntityDecl.ParameterEntityCategories.Datatype)
                    Debug.WriteLine("{3,5}:{0,30}{1,20}{2,30}", pe.ElementName, pe.Category, pe.ModuleName, pe.line);
            }
        }*/
	}
}
