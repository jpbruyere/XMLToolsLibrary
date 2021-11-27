using System;
using System.CodeDom;
using System.Diagnostics;
using System.Globalization;

namespace XMLTools {
	public class CCodeGenerator {
		public static void Process (XMLDocument dtd) {
			//foreach (EntityDecl ed in dtd.Entities)
			//	ENTITYProcessing (ed);
			foreach (ElementDecl e in dtd.Elements)
				ELEMENTProcessing (e);
			foreach (AttlistDecl ad in dtd.Attributes) 			
				ATTLISTProcessing (ad);

		}

		static void ENTITYProcessing (EntityDecl pe) {
			string mainModuleName;

			if (pe.IsValueEmpty)
				return;

			AttributeTypeDeclEnumerated attTypeEnum;

			switch (pe.Category) {
				case EntityDecl.ParameterEntityCategories.QName:
					Console.WriteLine (pe.ElementName);
					break;
				case EntityDecl.ParameterEntityCategories.Datatype:
					#region *.datatype
					//if (typeIsBuiltIn (pe.ElementName))
					//	break;

					//if (!pe.ExtractXMLObject (out attTypeEnum)) {
					//	//codeBase.Namespaces [0].Imports.Add (new CodeNamespaceImport (pe.ElementName + " = System.String"));
					//	createXMLAttributeClass (pe.ElementName);
					//}
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
		static void ELEMENTProcessing (ElementDecl e) {
			string name = "";
			string nameSpace  = "";
			if (e.Name is PEReference) {
				EntityDecl ed = (e.Name as PEReference).entityDecl;
				name = ed.ElementName;
			} else
				name = e.Name.ToString ();

			ContentSpec contentSpec = e.contentSpec.Extract<ContentSpec> ();

			switch (contentSpec.contentType) {
				case ContentTypes.Any:
					//CodeMemberProperty p = createListMember (cls, childrenName, elementBaseClass);
					//p.CustomAttributes.Add (new CodeAttributeDeclaration ("System.Xml.Serialization.XmlAnyElement"));
					break;
				case ContentTypes.Children:
					contentParticleProcessing (contentSpec);
					break;
				case ContentTypes.Mixed:
					//CreatePCDATAMembers (cls);
					contentParticleProcessing (contentSpec);
					break;
				case ContentTypes.Empty:
					break;
			}


			Console.WriteLine (name);
		}

		static void contentParticleProcessing (XMLToken _particle, int depth = 1) {
			Particle particle = _particle.Extract<Particle> ();
			if (particle == null) {
				ParticleBase pb = _particle.Extract<ParticleBase> ();

				Console.WriteLine(new string('\t',depth) + pb.CompiledName);
			} else {
				#region create mother class based on entity category
				if (_particle is PEReference) {
					EntityDecl ed = (_particle as PEReference).entityDecl;
					if (ed.Category == EntityDecl.ParameterEntityCategories.Class) {

						string name = ed.ElementName;
						Console.WriteLine ("-" + name);
					}
				}
				#endregion
				int curDepth = depth++;
				foreach (XMLToken tk in particle.Children)
					contentParticleProcessing (tk, curDepth);
			}
		}

		static void ATTLISTProcessing (AttlistDecl ald, CodeTypeDeclaration currentInterface = null, bool eventsMembers = false) {
			//bool isNewInterface = false;
			//CodeTypeDeclaration ctdNewInterface = null;
			string clsName = ald.CompiledName;

			Console.WriteLine ($"============= {ald.Name.ToString ()}; {clsName}===============");

			if (clsName.EndsWith ("events", true, CultureInfo.InvariantCulture))
				eventsMembers = true;

			if (ald.attributeDef.Count == 0)
				return;

			//if (ald.attributeDef.Count > 1 && ald.isCreatedByCompiler) {
			//	CodeTypeDeclaration ctdInterface = null;
			//	//create interface
			//	string InterfaceName =
			//		InterfacePrefix +
			//		clsName;

			//	if (currentInterface == null)
			//		cls.BaseTypes.Add (InterfaceName);
			//	else if (ctdInterface == null)
			//		currentInterface.BaseTypes.Add (InterfaceName);

			//	getTypeByName (InterfaceName, ref ctdInterface);

			//	//si l'interface n'existe pas encore
			//	if (ctdInterface == null)
			//		currentInterface = createInterface (InterfaceName);
			//	//	isNewInterface = true;

			//}

			foreach (XMLToken item in ald.attributeDef) {
				AttlistDecl alDecl = item.Extract<AttlistDecl> ();

				if (alDecl == null) {
					//Attdef processing
					//if (eventsMembers)
					//	ATTDEFEventsProcessing (item, cls, currentInterface);
					//else
						ATTDEFProcessing (item);
				} else
					ATTLISTProcessing (alDecl, currentInterface, eventsMembers);
			}

			Console.WriteLine ($"==============================================================");
			//si c'est une attlist dans une autre attlist, c'est forcément via une PEREference
			//				PEReference peRef = t as PEReference;
			//				//tweek to avoid xmlns
			//				if (string.Compare (aldElementName, "xmlns", true) == 0){
			//					ATTLISTProcessing (ald, cls, eventsMembers);
			//					continue;
			//				}


			//Stack<CodeRegionDirective> regions = new Stack<CodeRegionDirective> ();


		}

		static void ATTDEFProcessing (XMLToken t) {
			AttributeDef a = t.Extract<AttributeDef> ();
			DefaultDecl dd = a.defaultDecl.Extract<DefaultDecl> ();

			//process namespace attribute
			string[] xns = a.Name.ToString ().Split (new char[] { ':' });
			switch (xns[0]) {
				case "xmlns":
					//if (xns.Length == 1) {
					//	cls.CustomAttributes[XmlRootAttributeIndex].Arguments.Add (
					//		new CodeAttributeArgument ("Namespace", new CodePrimitiveExpression (dd.DefaultValue.ToString ())));
					//	cls.CustomAttributes[XmlTypeAttributeIndex].Arguments.Add (
					//		new CodeAttributeArgument ("Namespace", new CodePrimitiveExpression (dd.DefaultValue.ToString ())));
					//	return;
					//} else {
					//	Debug.WriteLine ("TODO: " + a.ToString ());
					//	return;
					//}
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


			string typeName = "";
			string enumName = "";



			if (a.attributeTypeDecl is PEReference) {
				typeName = (a.attributeTypeDecl as PEReference).entityDecl.ElementName;
				//enumName = enumPrefix + normalizeForCSHarp (typeName) + enumSufix;
			} else {
				//enumName = enumPrefix + normalizeForCSHarp (a.CompiledName) + enumSufix;
				typeName = "System.string";
			}

			Console.WriteLine ($"\t\t{typeName} {a.CompiledName}");

			//if (a.Name.ToString () != attName) {
			//	cmp.CustomAttributes.Add (
			//		new CodeAttributeDeclaration ("System.Xml.Serialization.XmlAttributeAttribute",
			//			new CodeAttributeArgument (new CodePrimitiveExpression (a.Name.ToString ()))));
			//}

			//cmp.Name = attName;
			//cmp.HasGet = true;
			//cmp.HasSet = true;

			//cmf.Name = fieldName;

			//AttributeTypeDeclEnumerated atde = a.attributeTypeDecl.Extract<AttributeTypeDeclEnumerated> ();
			//if (atde != null) {
			//	//enum
			//	//si ca référence une notation, il faudrait dériver les class des notations
			//	//d'une classe générique qui servirait de type pour cet attribut
			//	CodeTypeDeclaration ctdEnum = null;
			//	if (!(getEnumDeclaration (enumName, ref ctdEnum) || getEnumDeclaration (enumName, ref ctdEnum, cls))) {
			//		ctdEnum = createEnumeration (enumName, atde.tokenList);
			//		if (a.attributeTypeDecl.IsParameterEntityReference || t.IsParameterEntityReference || iFace != null)
			//			currentNameSpace.Types.Add (ctdEnum);
			//		else
			//			cls.Members.Add (ctdEnum);
			//	}

			//	cmp.Type = new CodeTypeReference (enumName);
			//	cmf.Type = new CodeTypeReference (enumName);

			//	if (dd.type != DefaultDeclTypes.IMPLIED &&
			//		dd.type != DefaultDeclTypes.REQUIRED) {
			//		cmp.CustomAttributes.Add (
			//			new CodeAttributeDeclaration ("System.ComponentModel.DefaultValue",
			//				new CodeAttributeArgument (
			//					new CodeFieldReferenceExpression (
			//						new CodeTypeReferenceExpression (enumName),
			//						normalizeForCSHarp (dd.DefaultValue.ToString ())))));
			//		cmf.InitExpression =
			//			new CodeFieldReferenceExpression (
			//				new CodeTypeReferenceExpression (enumName),
			//				normalizeForCSHarp (dd.DefaultValue.ToString ()));
			//	}
			//} else {

			//	AttributeTypeDeclTokenized atdt = a.attributeTypeDecl.Extract<AttributeTypeDeclTokenized> ();
			//	if (atdt != null) {
			//		//cmf.Comments.Add(new CodeCommentStatement("AttributeType is Tokenized"));
			//		switch (atdt.type) {
			//			case AttributeTypeDeclTokenized.TokenizedTypes.ID:
			//				cmf.Type = cmp.Type = new CodeTypeReference (IdBaseType);
			//				break;
			//			case AttributeTypeDeclTokenized.TokenizedTypes.IDREF:
			//				cmf.Type = cmp.Type = new CodeTypeReference (IdBaseType);
			//				break;
			//			case AttributeTypeDeclTokenized.TokenizedTypes.ENTITY:
			//				cmf.Type = cmp.Type = new CodeTypeReference (BaseType);
			//				break;
			//			case AttributeTypeDeclTokenized.TokenizedTypes.ENTITIES:
			//				CodeTypeReference ctr = new CodeTypeReference ("List");
			//				ctr.TypeArguments.Add (elementBaseClass);
			//				cmp.Type = ctr;
			//				cmp.Name = normalizeForCSHarp (a.Name.ToString (), true);
			//				break;
			//			case AttributeTypeDeclTokenized.TokenizedTypes.NMTOKEN:
			//				cmf.Type = cmp.Type = new CodeTypeReference ("System.String");
			//				break;
			//			case AttributeTypeDeclTokenized.TokenizedTypes.NMTOKENS:
			//				CodeTypeReference ctrTKS = new CodeTypeReference ("List");
			//				ctrTKS.TypeArguments.Add ("System.String");
			//				cmp.Type = ctrTKS;
			//				cmp.Name = normalizeForCSHarp (a.Name.ToString (), true);
			//				break;
			//		}
			//	} else {
			//		cmp.Type = new CodeTypeReference (typeName);
			//		cmf.Type = new CodeTypeReference (typeName);

			//		switch (dd.type) {
			//			case DefaultDeclTypes.NotSet:
			//				cmp.CustomAttributes.Add (
			//					new CodeAttributeDeclaration ("System.ComponentModel.DefaultValue",
			//						new CodeAttributeArgument (
			//							new CodePrimitiveExpression (dd.DefaultValue.ToString ()))));
			//				cmf.InitExpression =
			//					new CodePrimitiveExpression (dd.DefaultValue.ToString ());
			//				break;
			//			case DefaultDeclTypes.REQUIRED:
			//				break;
			//			case DefaultDeclTypes.IMPLIED:
			//				cmp.CustomAttributes.Add (
			//					new CodeAttributeDeclaration ("System.ComponentModel.DefaultValue",
			//						new CodeAttributeArgument (
			//							new CodePrimitiveExpression (""))));
			//				break;
			//			case DefaultDeclTypes.FIXED:
			//				//cmp.Attributes |= MemberAttributes.Const;
			//				//cmp.InitExpression = new CodePrimitiveExpression(a.defaultDecl.Extract<DefaultDecl>().DefaultValue.ToString());
			//				break;
			//			default:
			//				break;
			//		}
			//	}
			//}

		}

	}
}
