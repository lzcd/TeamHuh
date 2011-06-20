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



        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            var bindingName = binder.Name.Replace("_", "-").ToLower();

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
                result = new Query(
                    baseUrl: baseUrl,
                    username: username,
                    password: password,
                    document: new XDocument(selectedDecendants));
                return true;
            }

            result = null;
            return false;
        }

        private bool TryRetrieveChildDocument(XDocument parentDocument, out XDocument childDocument)
        {
            string childHref;
            var firstElement = parentDocument.Descendants().First();
            if (!TryFindAttributeValueByName("href", firstElement, out childHref))
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

            using (var stream = client.OpenRead(queryUrl))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings() { DtdProcessing = DtdProcessing.Ignore }))
            {
                return XDocument.Load(reader);
            }

            return null;
        }


        public IEnumerator GetEnumerator()
        {
            return GetAllChildren().GetEnumerator();
        }

        private List<Query> GetAllChildren()
        {
            var secondElement = document.Descendants().Skip(1);
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

                secondElement = childDocument.Descendants().Skip(1);
                childCount = secondElement.Count();
                if (childCount == 0)
                {
                    return new List<Query>();
                }
            }

            var childName = secondElement.First().Name.LocalName;
            var decendants = from decendant in document.Descendants(childName)
                             select new Query(
                               baseUrl: baseUrl,
                               username: username,
                               password: password,
                               document: new XDocument(decendant));
            return decendants.ToList();
        }

    }
}
