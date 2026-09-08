namespace Cinteros.Crm.Utils.Shuffle
{
    using Cinteros.Crm.Utils.Shuffle.Types;
    using Innofactor.Xrm.Utils.Common.Extensions;
    using Innofactor.Xrm.Utils.Common.Interfaces;
    using Innofactor.Xrm.Utils.Common.Misc;
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Xml;
    using System.Xml.Schema;
    using System.Xml.Serialization;

    /// <summary>Generic helper methods for Cinteros Shuffle</summary>
    public static class ShuffleHelper
    {
        #region Private Fields

        private static XmlSchemaSet schemas = null;

        #endregion Private Fields

        #region Public Properties

        /// <summary>
        /// 
        /// </summary>
        public static XmlSchemaSet Schemas
        {
            get
            {
                if (schemas == null)
                {
                    LoadDefinitionSchemas();
                }
                return schemas;
            }
        }

        #endregion Public Properties

        #region Public Methods

        /// <summary>Checks if given ShuffleDefinition contains data blocks, and thus requires data file to be used.</summary>
        /// <param name="definitionFile"></param>
        /// <returns></returns>
        public static bool DataFileRequired(string definitionFile)
        {
            if (!File.Exists(definitionFile))
            {
                return false;
            }
            var definition = GetShuffleDefinition(definitionFile, true);
            return DataFileRequired(definition);
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="elementname"></param>
        /// <returns></returns>
        public static string GetNodeDocumentation(string elementname)
        {
            var result = string.Empty;
            foreach (XmlSchema schema in Schemas.Schemas())
            {
                var particlename = string.Empty;
                if (elementname == "Relation")
                {
                    particlename = elementname;
                    elementname = "DataBlock";
                }
                var annotatedobject = GetAnnotatedObject(elementname, schema);
                if (!string.IsNullOrEmpty(particlename)
                    && annotatedobject is XmlSchemaComplexType particleparent
                    && particleparent.Particle is XmlSchemaSequence particlesequence)
                {
                    annotatedobject = particlesequence.Items
                        .Cast<XmlSchemaElement>()
                        .FirstOrDefault(i => i.Name == particlename);
                }

                var markups = annotatedobject?.Annotation?.Items
                    .Cast<XmlSchemaObject>()
                    .Where(a => a is XmlSchemaDocumentation)
                    .Select(a => (a as XmlSchemaDocumentation).Markup
                    .FirstOrDefault(m => m is XmlText) as XmlText)
                    .Where(m => m != null && m.Value != null)
                    .Select(m => m.Value);
                if (markups != null)
                {
                    result = string.Join("\r\n", markups);
                }
                if (!string.IsNullOrEmpty(result))
                {
                    break;
                }
            }
            return result;
        }

        /// <summary>Returns a list of file names that are required for the given ShuffleDefinition</summary>
        /// <param name="container"></param>
        /// <param name="shuffleDefinition">ShuffleDefinition file</param>
        /// <param name="definitionpath"></param>
        /// <returns>List of files</returns>
        public static List<string> GetReferencedFiles(IExecutionContainer container, string shuffleDefinition, string definitionpath)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            var result = new List<string>();
            if (File.Exists(shuffleDefinition))
            {
                var definition = GetShuffleDefinition(shuffleDefinition, true);
                if (DataFileRequired(definition))
                {
                    var datafile = Path.ChangeExtension(shuffleDefinition, ".data.xml");
                    container.Log($"Adding data file: {datafile}");
                    result.Add(datafile);
                }
                foreach (var solBlock in definition.Blocks.Items.Where(b => b is SolutionBlock))
                {
                    var solFile = GetSolutionFilename((SolutionBlock)solBlock, definitionpath);
                    if (!result.Contains(solFile))
                    {
                        container.Log($"Adding solution file: {solFile}");
                        result.Add(solFile);
                    }
                }
            }
            else
            {
                container.Log("Definition file not found");
            }
            container.Log($"Returning {result.Count} files");
            container.EndSection();
            return result;
        }

        /// <summary>Deserializes ShuffleDefinition XML to ShuffleDefinition class.</summary>
        /// <param name="definitionfile"></param>
        /// <param name="clearVariables">Set this to remove any {ShuffleVar:xxx} variables in the definition file</param>
        /// <returns></returns>
        public static ShuffleDefinition GetShuffleDefinition(string definitionfile, bool clearVariables)
        {
            try
            {
                var deserializer = new XmlSerializer(typeof(ShuffleDefinition));
                string content;
                using (var fileReader = new StreamReader(definitionfile))
                {
                    content = fileReader.ReadToEnd();
                }
                content = VerifyShuffleVars(content, clearVariables);
                using (var stringReader = new StringReader(content))
                {
                    return (ShuffleDefinition)deserializer.Deserialize(stringReader);
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary></summary>
        /// <param name="dataFile"></param>
        /// <returns></returns>
        public static XmlDocument LoadDataFile(string dataFile)
        {
            var data = new XmlDocument();
            try
            {
                data.Load(dataFile);
            }
            catch (XmlException)
            {
                using (var sr = new StreamReader(dataFile))
                {
                    var textfile = sr.ReadToEnd();
                    data = StringToXmlSerialized(textfile);
                }
            }
            catch (FileNotFoundException)
            {
                data = null;
            }
            return data;
        }

        /// <summary>Helper method to embed any type of serialized data into XmlDocument for import into CRM</summary>
        /// <param name="SerializedData"></param>
        /// <returns></returns>
        public static XmlDocument StringToXmlSerialized(string SerializedData)
        {
            return StringToXmlSerialized(SerializedData, '\0');
        }

        /// <summary>Helper method to embed any type of serialized data into XmlDocument for import into CRM</summary>
        /// <param name="SerializedData"></param>
        /// <param name="delimeter"></param>
        /// <returns></returns>
        public static XmlDocument StringToXmlSerialized(string SerializedData, char delimeter)
        {
            var data = new XmlDocument();
            try
            {
                data.LoadXml(SerializedData);
            }
            catch (XmlException)
            {
                data = TextFileToXmlSerialized(SerializedData, delimeter);
            }
            return data;
        }

        /// <summary>Validates given ShuffleDefinition with XSD.</summary>
        /// <param name="def"></param>
        /// <returns></returns>
        public static void ValidateDefinitionXml(XmlDocument def)
        {
            ValidateDefinitionXml(null, def);
        }

        /// <summary>Validates given ShuffleDefinition with XSD.</summary>
        /// <param name="container"></param>
        /// <param name="def"></param>
        /// <returns></returns>
        public static void ValidateDefinitionXml(IExecutionContainer container, XmlDocument def)
        {
            try
            {
                def.Schemas = Schemas;
                if (def.Schemas.Count >= 2)
                {
                    def.Validate(null);

                    container?.Log("ShuffleDefinition validated");
                }
            }
            catch (XmlSchemaValidationException ex)
            {
                container?.Log(ex);
                throw;
            }
        }

        /// <summary>Verifies that no {ShuffleVar:xxx} exist in the definition</summary>
        /// <param name="definition"></param>
        /// <param name="clearExisting">Set this to first try to remove all existing variables</param>
        /// <returns></returns>
        public static string VerifyShuffleVars(string definition, bool clearExisting)
        {
            if (clearExisting)
            {
                while (definition.Contains("{ShuffleVar:"))
                {
                    var variable = definition.Substring(definition.IndexOf("{ShuffleVar:", StringComparison.InvariantCulture));
                    variable = variable.Split('}')[0] + "}";
                    definition = definition.Replace(variable, "");
                }
            }
            if (definition.Contains("{ShuffleVar:"))
            {
                var variable = definition.Substring(definition.IndexOf("{ShuffleVar:", StringComparison.InvariantCulture) + 12);
                variable = variable.Split('}')[0];
                throw new Exception("Missing replacement value for variable: " + variable);
            }
            return definition;
        }

        /// <summary>Verifies that no {ShuffleVar:xxx} exist in the definition</summary>
        /// <param name="xDefinitionDoc"></param>
        /// <param name="clearExisting">Set this to first try to remove all existing variables</param>
        public static void VerifyShuffleVars(XmlDocument xDefinitionDoc, bool clearExisting)
        {
            var xml = xDefinitionDoc.InnerXml;
            xml = VerifyShuffleVars(xml, clearExisting);
            xDefinitionDoc.InnerXml = xml;
        }

        /// <summary>
        /// Extracts delimeted text block from XML document format
        /// </summary>
        /// <param name="xml">XML document containing the text block</param>
        /// <returns>Block with text-serialized CRM data</returns>
        public static string XmlSerializedToTextFile(XmlDocument xml)
        {
            var root = XML.FindChild(xml, "ShuffleData");
            var sertype = XML.GetAttribute(root, "Type");
            if (sertype == SerializationType.Text.ToString())
            {
                var xText = XML.FindChild(root, "Text");
                var text = xText.InnerText;
                return text;
            }
            else
            {
                throw new ArgumentOutOfRangeException("xml", sertype, "XmlSerializedToTextFile called with xml document of incompatible serialization type");
            }
        }

        #endregion Public Methods

        #region Private Methods

        private static bool DataFileRequired(ShuffleDefinition definition)
        {
            return definition != null && definition.Blocks != null && definition.Blocks.Items != null && definition.Blocks.Items.Any(b => b is DataBlock);
        }

        private static XmlSchemaAnnotated GetAnnotatedObject(string elementname, XmlSchema schema)
        {
            XmlSchemaAnnotated annotatedobject = schema.Elements.Values
                .Cast<XmlSchemaElement>()
                .FirstOrDefault(i => i.Name == elementname);

            if (annotatedobject == null)
            {
                annotatedobject = schema.SchemaTypes.Values
                    .Cast<XmlSchemaType>()
                    .FirstOrDefault(i => i.Name == elementname);
            }

            return annotatedobject;
        }

        private static string GetSolutionFilename(SolutionBlock solBlock, string definitionpath)
        {
            var file = solBlock.File;
            if (string.IsNullOrWhiteSpace(file))
            {
                file = solBlock.Name;
            }
            var path = solBlock.Path;
            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(definitionpath))
            {
                path = definitionpath;
            }
            path += path.EndsWith("\\") ? "" : "\\";
            string filename;
            if (solBlock.Import.Type == SolutionTypes.Managed)
            {
                filename = path + file + "_managed.zip";
            }
            else if (solBlock.Import.Type == SolutionTypes.Unmanaged)
            {
                filename = path + file + ".zip";
            }
            else
            {
                throw new ArgumentOutOfRangeException("Type", solBlock.Import.Type, "Invalid Solution type");
            }

            if (filename.Contains("%"))
            {
                var envvars = Environment.GetEnvironmentVariables();
                foreach (DictionaryEntry de in envvars)
                {
                    filename = filename.Replace("%" + de.Key.ToString() + "%", de.Value.ToString());
                }
            }
            return filename;
        }

        private static void LoadDefinitionSchemas()
        {
            schemas = new XmlSchemaSet();
            var assembly = Assembly.GetExecutingAssembly();
            var shufdefresource = assembly.GetManifestResourceNames().Where(n => n.ToLowerInvariant().EndsWith("shuffledefinition.xsd")).FirstOrDefault();
            var qryexpresource = assembly.GetManifestResourceNames().Where(n => n.ToLowerInvariant().EndsWith("queryexpression.xsd")).FirstOrDefault();

            var stream = assembly.GetManifestResourceStream(shufdefresource);
            if (stream != null)
            {
                schemas.Add(null, XmlReader.Create(stream));
            }
            stream = assembly.GetManifestResourceStream(qryexpresource);
            if (stream != null)
            {
                schemas.Add(null, XmlReader.Create(stream));
            }
        }

        /// <summary>Helper method to embed delimeted text file into XmlDocument for import into CRM</summary>
        /// <param name="text"></param>
        /// <param name="delimeter"></param>
        /// <returns></returns>
        private static XmlDocument TextFileToXmlSerialized(string text, char delimeter)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentNullException("text", "Data string is empty, cannot compose serialized block");
            }
            if (delimeter == '\0')
            {
                var reader = new StringReader(text);
                if (reader.ReadLine() != null)  // Första raden innehöll nåt
                {
                    var cols = reader.ReadLine();    // Läs andra raden
                    if (cols != null && cols.StartsWith("Entity") && cols.Length > 6)
                    {
                        delimeter = cols[6];
                    }
                }
            }
            if (delimeter == '\0')
            {
                throw new ArgumentNullException("delimeter", "Cannot parse delimeter from data string");
            }
            var xml = new XmlDocument();
            XmlNode root = xml.CreateElement("ShuffleData");
            xml.AppendChild(root);
            XML.AppendAttribute(root, "Type", SerializationType.Text.ToString());
            XML.AppendAttribute(root, "Delimeter", delimeter.ToString());
            XML.AddCDATANode(root, "Text", text);
            return xml;
        }

        #endregion Private Methods
    }
}