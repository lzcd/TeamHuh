using System;
using System.Collections;
using System.Dynamic;
using System.Linq;
using System.Net;
using System.Xml;
using System.Xml.Linq;

namespace TeamHuh
{
    public class Query : DynamicObject, IEnumerable
    {
        private string baseUrl;
        private string username;
        private string password;

        public Query(string baseUrl, string username, string password)
        {
            this.baseUrl = baseUrl;
            this.username = username;
            this.password = password;
        }

        private XDocument document;
        protected object differedValue;
        protected bool hasDifferedValue;

        protected Query(
            string baseUrl,
            string username, string password,
            XDocument document,
            string nestedName = null)
            : this(baseUrl, username, password)
        {
            if (string.IsNullOrEmpty(nestedName))
            {
                this.document = document;
            }
            else
            {
                var selected = default(XElement);
                if (selected == null)
                {
                    TryFindDecendant(nestedName, document, out selected);
                }

                if (selected == null)
                {
                    var attributeValue = default(string);
                    if (TryFindAttributeValueByName(nestedName, document.Descendants().First(), out attributeValue))
                    {
                        differedValue = attributeValue;
                        hasDifferedValue = true;
                        return;

                    }
                }

                this.document = new XDocument(selected);

            }

        }

        WebClient client;

        public override bool TryGetMember(GetMemberBinder binder, out object result)
        {
            if (binder.Name.Equals("exists", StringComparison.CurrentCultureIgnoreCase))
            {
                if (hasDifferedValue)
                {
                    result = true;
                    return true;
                }

                result = (document.Descendants().Count() > 0);
                return true;
            }

            if (hasDifferedValue)
            {
                result = differedValue;
                return true;
            }



            if (binder.Name.Equals("first", StringComparison.CurrentCultureIgnoreCase))
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

            var queryUrl = default(string);
            var nestedName = default(string);

            if (document != null)
            {
                var selected = default(XElement);

                if (selected == null &&
                    TryFindDecendant(binder.Name, document, out selected))
                {
                    result = new Query(
                        baseUrl: baseUrl,
                        username: username,
                        password: password,
                        document: new XDocument(selected),
                        nestedName: null);
                    return true;
                }

                if (selected == null)
                {
                    TryFindById(binder.Name, document, out selected);
                }

                if (selected == null)
                {
                    selected = document.Elements().First();
                }


                var attributeValue = default(string);
                if (TryFindAttributeValueByName(binder.Name, selected, out attributeValue))
                {
                    result = attributeValue;
                    return true;
                }

                nestedName = binder.Name;
                var href = default(string);
                TryFindAttributeValueByName("href", selected, out href);
                queryUrl = baseUrl + href;
            }
            else
            {
                queryUrl = baseUrl + @"/httpAuth/app/rest/" + binder.Name.ToLower();
            }

            var childDocument = default(XDocument);
            TryLoadXml(queryUrl, out childDocument);

            var resultQuery = new Query(
                       nestedName: nestedName,
                       baseUrl: baseUrl,
                       username: username,
                       password: password,
                       document: childDocument);

            if (resultQuery.hasDifferedValue)
            {
                result = resultQuery.differedValue;
                return true;
            }

            result = resultQuery;
            return true;
            //return base.TryGetMember(binder, out result);
        }

        private bool TryLoadXml(string queryUrl, out XDocument document)
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
                document = XDocument.Load(reader);
            }

            return true;
        }

        private bool TryFindDecendant(string name, XDocument document, out XElement selectedDecendant)
        {
            selectedDecendant = (from item in document.Descendants()
                                 where item.Name.LocalName.Equals(name, StringComparison.CurrentCultureIgnoreCase)
                                 select item).SingleOrDefault();

            return (selectedDecendant != null);
        }

        private bool TryFindById(string name, XDocument document, out XElement selectedItem)
        {
            selectedItem = (from items in document.Elements()
                            from item in items.Elements()
                            from attribute in item.Attributes()
                            where attribute.Name.LocalName.Equals("id", StringComparison.CurrentCultureIgnoreCase) &&
                                  attribute.Value.Equals(name, StringComparison.CurrentCultureIgnoreCase)
                            select item).SingleOrDefault();

            return (selectedItem != null);
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

        public IEnumerator GetEnumerator()
        {
            var allDecendants = document.Descendants();
            switch (allDecendants.Count())
            {
                case 0:
                    return "".GetEnumerator();
                case 1:
                    var href = default(string);
                    if (!TryFindAttributeValueByName("href", allDecendants.First(), out href))
                    {
                        return "".GetEnumerator();
                    }
                    var queryUrl = baseUrl + href;

                    var childDocument = default(XDocument);
                    TryLoadXml(queryUrl, out childDocument);

                    var result = new Query(
                               baseUrl: baseUrl,
                               username: username,
                               password: password,
                               document: childDocument);

                    return result.GetEnumerator();
                default:
                    var childName = allDecendants.Skip(1).First().Name.LocalName;
                    var decendants = from decendant in document.Descendants(childName)
                                     select new Query(
                                       baseUrl: baseUrl,
                                       username: username,
                                       password: password,
                                       document: new XDocument(decendant));
                    return decendants.GetEnumerator();
            }


        }
    }
}
