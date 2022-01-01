// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace XMLTools
{
	public class XMLToken
	{
        public XMLToken(){}
        public XMLToken(string v)
        { _value = v; }

        string _value;

        public string value
        {
            get { return _value; }
            set { _value = value; }
        }
		public string Value => this is PEReference ? (this as PEReference).CompiledValue.ToString() : _value;
        /// <summary>
        /// XMLToken: convert to T and if it's a PEReference, takes the compiled value and convert it
        /// return default(T) is not the token or a compiled value is targeted type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public T Extract<T>()
        {
			if (this is T) {
				try {
					return (T)Convert.ChangeType (this, typeof(T));
				} catch (Exception ex) {
					return (T)(object)this;
				}
			}

			PEReference pe = this as PEReference;
			if (pe != null) {
				if (pe.CompiledValue is T){
					try {
						return (T)Convert.ChangeType(pe.CompiledValue, typeof(T));
					} catch (Exception ex) {
						return (T)(object)pe.CompiledValue;
					}
				}
            }

            return default(T);
        }
		public bool Extract2<T>(out T result)
		{
			if (this is T) {
				try {
					result = (T)Convert.ChangeType (this, typeof(T));
				} catch (Exception ex) {
					result = (T)(object)this;
				}
				return true;
			}

			if (this is PEReference pe)	{
				if (pe.CompiledValue is T){
					try {
						result = (T)Convert.ChangeType(pe.CompiledValue, typeof(T));
					} catch (Exception ex) {
						result = (T)(object)pe.CompiledValue;
					}
					return true;
				}
			}

			result = default(T);
			return false;
		}
        /// <summary>
        /// XMLToken: return ElementName from Entity if it's a PEReference, else
        /// return DTDObject.Name.ToString();
        /// return "unamed" if it's a simple xmltoken
        /// </summary>
        public virtual string CompiledName
        {
            get
            {
				if (this is AttlistDecl) {
					//ATTLIST could have the name of a modularized PERef if it's
					//created by compiler
					AttlistDecl ad = this as AttlistDecl;
					return ad.isCreatedByCompiler ?
						EntityDecl.GetElementNameFromPEName (ad.Name) :
						ad.Name.CompiledName;
				}
				if (this is DTDObject)
					return (this as DTDObject).Name.CompiledName;
				else if (this is PEReference)
					return (this as PEReference).entityDecl.ElementName;
				else
                    return string.IsNullOrEmpty(value) ? "unamed" : value;
            }
        }
        public static implicit operator string(XMLToken t)
        {
            return t == null ? null : t.value;
        }
        //attention que si le token est dans une liste ou une stack, il faut mettre à jour explicitement
        //value sinon on ne met pas à jour le token dans la liste puisqu'un nouveau token est ici créé!!
        public static implicit operator XMLToken(string s)
        { return new XMLToken { _value = s }; }

        public override string ToString()
        {
            if (this is PEReference)
                return (this as PEReference).ToString();

            return value;
        }
    }
}
