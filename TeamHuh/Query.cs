using System;
using System.Collections;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;

namespace TeamHuh
{
    public class Query : DynamicObject, IEnumerable
    {
        private string baseUrl;
        private string username;
        private string password;
        private XDocument document;
        private XDocument childDocument;

        public Query(
            string baseUrl,
            string username,
            string password,
            XDocument document = null)
        {
            this.baseUrl = baseUrl;
            this.username = username;
            this.password = password;
            this.document = document;
        }

        const string existsKeyword = "exists";

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var bindingName = binder.Name.Replace("_", "-").ToLower();


            if (bindingName.EndsWith(existsKeyword))
            {
                bindingName = bindingName.Substring(0, bindingName.Length - existsKeyword.Length);

                object unusedResult;
                result = TryFind(bindingName, out unusedResult);
                return true;
            }

            return TryFind(bindingName, out result);
        }

        private bool TryFind(string bindingName, out object result)
        {
            if (document == null)
            {
                var queryUrl = baseUrl + @"/httpAuth/app/rest/" + bindingName;
                document = Retrieve(queryUrl);
                result = new Query(
                    baseUrl: baseUrl,
                    username: username,
                    password: password,
                    document: document);
                return true;
            }

            if (bindingName.Equals("first", StringComparison.CurrentCultureIgnoreCase))
            {
                var enumerator = GetEnumerator();
                if (enumerator.MoveNext())
                {
                    result = enumerator.Current;
                    return true;
                }

                result = null;
                return true;
            }

            string value;
            IEnumerable<XElement> selectedDecendants;
            if (!TryFind(bindingName, document, out value, out selectedDecendants))
            {
                if (childDocument == null)
                {
                    if (!TryRetrieveChildDocument(document, out childDocument))
                    {
                        result = null;
                        return false;
                    }
                }

                if (childDocument == null)
                {
                    result = null;
                    return false;
                }

                var firstChildElement = childDocument.Descendants().First();

                if (!TryFind(bindingName, childDocument, out value, out selectedDecendants))
                {
                    result = null;
                    return false;
                }
            }

            if (value != null)
            {
                result = value;
                return true;
            }

            if (selectedDecendants != null)
            {
                if (selectedDecendants.Count() == 1 &&
                    !selectedDecendants.First().HasElements &&
                    bindingName.Equals(selectedDecendants.First().Name.LocalName, StringComparison.CurrentCultureIgnoreCase))
                {
                    XDocument stepChildDocument;
                    if (TryRetrieveChildDocument(selectedDecendants.First(), out stepChildDocument))
                    {
                        result = new Query(
                            baseUrl: baseUrl,
                            username: username,
                            password: password,
                            document: stepChildDocument);
                        return true;
                    }
                    else
                    {
                        result = selectedDecendants.First().Value;
                        return true;
                    }
                }
                else if (selectedDecendants.Count() == 1)
                {
                    result = new Query(
                       baseUrl: baseUrl,
                       username: username,
                       password: password,
                       document: new XDocument(selectedDecendants));
                    return true;
                }
                else
                {
                    var newDoc = new XDocument();
                    var rootElement = new XElement(bindingName + "s");
                    rootElement.Add(selectedDecendants);
                    newDoc.AddFirst(rootElement);
                    result = new Query(
                        baseUrl: baseUrl,
                        username: username,
                        password: password,
                        document: newDoc);
                    return true;
                }
            }

            result = null;
            return false;
        }

        private bool TryRetrieveChildDocument(XDocument parentDocument, out XDocument childDocument)
        {
            var firstElement = parentDocument.Descendants().First();
            return TryRetrieveChildDocument(firstElement, out childDocument);
        }

        private bool TryRetrieveChildDocument(XElement element, out XDocument childDocument)
        {
            string childHref;

            if (!TryFindAttributeValueByName("href", element, out childHref))
            {
                childDocument = null;
                return false;
            }

            var queryUrl = baseUrl + childHref;
            childDocument = Retrieve(queryUrl);
            return true;
        }

        private bool TryFind(string bindingName, XDocument parentDocument, out string value, out IEnumerable<XElement> selectedDecendants)
        {
            value = null;
            selectedDecendants = null;
            var firstElement = parentDocument.Descendants().First();
            return (TryFindAttributeValueByName(bindingName, firstElement, out value) ||
                    TryFindDecendants(bindingName, parentDocument, out selectedDecendants));
        }


        private bool TryFindDecendants(string name, XDocument doc, out IEnumerable<XElement> selectedDecendants)
        {
            selectedDecendants = from item in doc.Descendants()
                                 where item.Name.LocalName.Equals(name, StringComparison.CurrentCultureIgnoreCase)
                                 select item;

            if (selectedDecendants.Count() == 0)
            {
                selectedDecendants = null;
                return false;
            }

            return true;
        }

        private bool TryFindAttributeValueByName(string name, XElement element, out string selectedAttributeValue)
        {
            var valueByName = (from attribute in element.Attributes()
                               select new
                               {
                                   Key = attribute.Name.LocalName.ToLower(),
                                   Value = attribute.Value
                               }).ToDictionary(k => k.Key, v => v.Value);

            return valueByName.TryGetValue(name.ToLower(), out selectedAttributeValue);
        }

        private WebClient client;

        private XDocument Retrieve(string queryUrl)
        {
            if (client == null)
            {
                client = new WebClient();
                client.Credentials = new NetworkCredential(username, password);
                client.Headers.Add("Accepts:text/xml");
            }

            try
            {
                using (var stream = client.OpenRead(queryUrl))
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore }))
                {
                    return XDocument.Load(reader);
                }
            }
            catch
            {
                return null;
            }
        }


        public IEnumerator GetEnumerator()
        {
            return GetAllChildren().GetEnumerator();
        }

        private List<Query> GetAllChildren()
        {
            var parentDocument = document;
            var secondElement = parentDocument.Descendants().Skip(1);
            var childCount = secondElement.Count();
            if (childCount == 0)
            {
                if (childDocument == null)
                {
                    if (!TryRetrieveChildDocument(document, out childDocument))
                    {
                        return new List<Query>();
                    }
                }

                parentDocument = childDocument;
                secondElement = parentDocument.Descendants().Skip(1);
                childCount = secondElement.Count();
                if (childCount == 0)
                {
                    return new List<Query>();
                }
            }

            var childName = secondElement.First().Name.LocalName;
            var decendants = from decendant in parentDocument.Descendants(childName)
                             select new Query(
                               baseUrl: baseUrl,
                               username: username,
                               password: password,
                               document: new XDocument(decendant));
            return decendants.ToList();
        }

    }
}
