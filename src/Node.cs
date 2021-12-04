// Copyright (c) 2013-2021  Jean-Philippe Bruyère <jp_bruyere@hotmail.com>
//
// This code is licensed under the MIT license (MIT) (http://opensource.org/licenses/MIT)

using System;
using System.Collections.Generic;
using System.Linq;
namespace XMLTools
{
    public static class extentions
    {
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>
                (this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> knownKeys = new HashSet<TKey>();
            foreach (TSource element in source)
            {
                if (knownKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }
    }
    public class node
    {
        public string Name;
        public node Parent;
        public List<node> Children = new List<node>();
        public List<Attribute> Attributes = new List<Attribute>();
        public string content = string.Empty;

        public int depth
        {
            get
            {
                int d = 0;
                node tmp = Parent;
                while (tmp != null)
                {
                    tmp = tmp.Parent;
                    d++;
                }
                return d;
            }
        }
        public void addChild(node child)
        {
            if (child == null)
                return;

            child.Parent = this;
            Children.Add(child);
        }

        public void removeChild(node child)
        {
            Children.Remove(child);
        }

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

        public node[] leafs
        {
            get
            {
                if (Children.Count == 0)
                    return new node[] { this };

                List<node> leafs = new List<node>();

                foreach (node n in Children)
                {
                    leafs.AddRange(n.leafs);
                }
                return leafs.ToArray();
            }
        }
        public string[] AllNodeNames
        {
            get
            {
                if (Children.Count == 0)
                    return new string[] { this.Name };

                List<string> names = new List<string>();
                names.Add(Name);

                foreach (node n in Children)
                {
                    names.AddRange(n.AllNodeNames);
                }

                return names.ToArray();
            }
        }
    }
}
