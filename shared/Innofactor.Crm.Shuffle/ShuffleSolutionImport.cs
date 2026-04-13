namespace Cinteros.Crm.Utils.Shuffle
{
    using Cinteros.Crm.Utils.Shuffle.Types;
    using Innofactor.Xrm.Utils.Common.Extensions;
    using Innofactor.Xrm.Utils.Common.Interfaces;
    using Innofactor.Xrm.Utils.Common.Misc;
    using System.IO.Compression;
    using Microsoft.Crm.Sdk.Messages;
    using Microsoft.Xrm.Sdk;
    using Microsoft.Xrm.Sdk.Messages;
    using Microsoft.Xrm.Sdk.Query;
    using System;
    using System.Collections;
    using System.IO;
    using System.Reflection;
    using System.Xml;

    public partial class Shuffler
    {
        #region Private Methods

        private static string ExtractErrorMessage(string message)
        {
            const string fault = "(Fault Detail is equal to Microsoft.Xrm.Sdk.OrganizationServiceFault).: ";
            const string unhandled = "Unhandled Exception: ";
            while (message.Contains(fault))
            {
                message = message.Substring(message.IndexOf(fault, StringComparison.InvariantCultureIgnoreCase) + fault.Length);
            }
            message = message.Replace("&lt;", "<").Replace("&gt;", ">");
            while (message.Contains("<InnerFault>") && message.Contains("</InnerFault>"))
            {
                message = message.Substring(message.IndexOf("<InnerFault>", StringComparison.InvariantCultureIgnoreCase) + 9);
                message = message.Substring(0, message.LastIndexOf("</InnerFault>", StringComparison.InvariantCultureIgnoreCase));
            }
            if (message.Contains("<Message>") && message.Contains("</Message>"))
            {
                message = message.Substring(message.IndexOf("<Message>", StringComparison.InvariantCultureIgnoreCase) + 9);
                message = message.Substring(0, message.LastIndexOf("</Message>", StringComparison.InvariantCultureIgnoreCase));
            }
            else if (message.StartsWith(unhandled))
            {
                message = message.Substring(message.IndexOf(unhandled, StringComparison.InvariantCultureIgnoreCase) + unhandled.Length);
                if (message.Contains(":"))
                {
                    message = message.Split(':')[0];
                }
            }
            return message;
        }

        private SolutionImportConditions CheckIfImportRequired(IExecutionContainer container, SolutionBlockImport import, string name, Version thisversion)
        {
            container.StartSection("CheckIfImportRequired");
            var result = SolutionImportConditions.Create;
            var overwritesame = import.OverwriteSameVersion;
            var overwritenewer = import.OverwriteNewerVersion;
            var cSolutions = GetExistingSolutions(container);
            foreach (var cdSolution in cSolutions.Entities)
            {
                if (cdSolution.GetAttribute("uniquename", "") == name)
                {   // Now we have found the same solution in target environment
                    result = SolutionImportConditions.Update;
                    var existingversion = new Version(cdSolution.GetAttribute("version", "1.0.0.0"));
                    container.Log("Existing solution has version: {0}", existingversion);
                    var comparison = thisversion.CompareTo(existingversion);
                    if (!overwritesame && comparison == 0)
                    {
                        result = SolutionImportConditions.Skip;
                        SendLine(container, "Solution {0} {1} already exists in target", name, thisversion);
                    }
                    else if (!overwritenewer && comparison < 0)
                    {
                        result = SolutionImportConditions.Skip;
                        SendLine(container, "Existing solution {0} {1} is newer than {2}", name, existingversion, thisversion);
                    }
                    else if (existingversion == thisversion)
                    {
                        SendLine(container, "Updating version {0}", thisversion);
                    }
                    else
                    {
                        SendLine(container, "Replacing version {0} with {1}", existingversion, thisversion);
                    }
                    break;
                }
            }
            container.Log("Import Condition: {0}", result);
            container.EndSection();
            return result;
        }

        private bool DoImportSolution(SolutionBlockImport import, string filename, Version version)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            var result = false;
            var activatecode = import.ActivateServersideCode;
            var overwrite = import.OverwriteCustomizations;
            Exception ex = null;
            SendLine(container, "Importing solution: {0} Version: {1}", filename, version);
            var fileBytes = File.ReadAllBytes(filename);
            var impSolReq = new ImportSolutionRequest()
            {
                CustomizationFile = fileBytes,
                OverwriteUnmanagedCustomizations = overwrite,
                PublishWorkflows = activatecode,
                ImportJobId = Guid.NewGuid()
            };

            if (/*crmsvc is CrmServiceProxy && */container.GetCrmVersion().Major >= 6) //((CrmServiceProxy)crmsvc).CrmVersion.Major >= 6)
            {   // CRM 2013 or later, import async
                result = DoImportSolutionAsync(impSolReq, ref ex);
            }
            else
            {   // Pre CRM 2013, import sync
                result = DoImportSolutionSync(impSolReq, ref ex);
            }
            if (!result && stoponerror)
            {
                if (ex != null)
                {
                    throw ex;
                }
                else
                {
                    throw new Exception("Solution import failed");
                }
            }
            container.Log($"Returning: {result}");
            container.EndSection();
            return result;
        }

        private bool DoImportSolutionAsync(ImportSolutionRequest impSolReq, ref Exception ex)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            // Code cred to Wael Hamze
            // http://waelhamze.com/2013/11/17/asynchronous-solution-import-dynamics-crm-2013/
            var result = false;
            var asyncRequest = new ExecuteAsyncRequest()
            {
                Request = impSolReq
            };
            var asyncResponse = container.Execute(asyncRequest) as ExecuteAsyncResponse;
            var asyncJobId = asyncResponse.AsyncJobId;
            var end = DateTime.MaxValue;
            var importStatus = -1;
            var progress = 0;
            var statustext = "Submitting job";
            SendLineUpdate(container, $"Import status: {statustext}");
            while (end >= DateTime.Now)
            {
                Entity cdAsyncOperation = null;
                try
                {
                    cdAsyncOperation = container.Retrieve(SystemJob.EntityName, asyncJobId,
                        new ColumnSet(SystemJob.PrimaryKey, SystemJob.Status, SystemJob.StatusReason, SystemJob.Message, SystemJob.Friendlymessage));
                }
                catch (Exception asyncex)
                {
                    cdAsyncOperation = null;
                    container.Log(asyncex);
                    container.EndSection();   // Ending section started by Retrieve above to prevent indentation inflation
                }
                if (cdAsyncOperation != null)
                {
                    container.Attribute(SystemJob.StatusReason).On(cdAsyncOperation).ToString();
                    statustext = container.Attribute(SystemJob.StatusReason).On(cdAsyncOperation).ToString();
                    var newStatus = cdAsyncOperation.GetAttribute(SystemJob.StatusReason, new OptionSetValue()).Value;
                    if (newStatus != importStatus)
                    {
                        importStatus = newStatus;
                        if (end.Equals(DateTime.MaxValue) && importStatus != (int)SystemJob.StatusReason_OptionSet.Waiting)
                        {
                            end = timeout > 0 ? DateTime.Now.AddMinutes(timeout) : DateTime.Now.AddMinutes(2);
                            SendLineUpdate(container, "Import job picked up at {0}", DateTime.Now);
                            container.Log("Timout until: {0}", end.ToString("HH:mm:ss.fff"));
                            SendLine(container, "Import status: {0}", statustext);
                        }
                        SendLineUpdate(container, "Import status: {0}", statustext);
                        container.Log("Import message:\n{0}", cdAsyncOperation.GetAttribute(SystemJob.Message, "<none>"));
                        if (importStatus == (int)SystemJob.StatusReason_OptionSet.Succeeded)
                        {   // Succeeded
                            result = true;
                            break;
                        }
                        else if (importStatus == (int)SystemJob.StatusReason_OptionSet.Pausing
                            || importStatus == (int)SystemJob.StatusReason_OptionSet.Canceling
                            || importStatus == (int)SystemJob.StatusReason_OptionSet.Failed
                            || importStatus == (int)SystemJob.StatusReason_OptionSet.Canceled)
                        {   // Error statuses
                            var friendlymessage = cdAsyncOperation.GetAttribute(SystemJob.Friendlymessage, "");
                            SendLine(container, "Message: {0}", friendlymessage);
                            if (friendlymessage == "Access is denied.")
                            {
                                SendLine(container, "When importing to onprem environment, the async service user must be granted read/write permission to folder:");
                                SendLine(container, "  C:\\Program Files\\Microsoft Dynamics CRM\\CustomizationImport");
                            }
                            else
                            {
                                var message = cdAsyncOperation.GetAttribute(SystemJob.Message, "<none>");
                                message = ExtractErrorMessage(message);
                                if (!string.IsNullOrWhiteSpace(message) && !message.Equals(friendlymessage, StringComparison.InvariantCultureIgnoreCase))
                                {
                                    SendLine(container, "Detailed message: \n{0}", message);
                                }
                                else
                                {
                                    SendLine(container, "See log file for technical details.");
                                }
                            }
                            container.Attribute(SystemJob.Status).On(cdAsyncOperation).ToString();

                            ex = new Exception($"Solution Import Failed: {container.Attribute(SystemJob.Status).On(cdAsyncOperation).ToString()} - {container.Attribute(SystemJob.StatusReason).On(cdAsyncOperation).ToString()}");

                            break;
                        }
                    }
                }
                System.Threading.Thread.Sleep(1000);
                if (importStatus == 20)
                {   // In progress, read percent
                    try
                    {
                        var job = container.Retrieve(ImportJob.EntityName, impSolReq.ImportJobId, new ColumnSet(ImportJob.Progress));
                        if (job != null)
                        {
                            var newProgress = Convert.ToInt32(Math.Round(job.GetAttribute(ImportJob.Progress, 0D)));
                            if (newProgress > progress)
                            {
                                progress = newProgress;
                                SendStatus(-1, -1, 100, progress);
                                SendLineUpdate(container, "Import status: {0} - {1}%", statustext, progress);
                            }
                        }
                    }
                    catch (Exception jobex)
                    {   // We probably tried before the job was created
                        if (jobex.Message.ToUpperInvariant().Contains("DOES NOT EXIST"))
                        {
                            container.Log("Importjob not created yet or already deleted");
                        }
                        else
                        {
                            container.Log(jobex);
                        }
                        container.EndSection();   // Ending section started by Retrieve above to prevent indentation inflation
                    }
                }
            }
            if (end < DateTime.Now)
            {
                SendLine(container, "Import timed out.");
            }
            SendStatus(-1, -1, 100, 0);
            container.EndSection();
            return result;
        }

        private bool DoImportSolutionSync(ImportSolutionRequest impSolReq, ref Exception ex)
        {
            container.StartSection(MethodBase.GetCurrentMethod().Name);
            bool result;
            try
            {
                container.Execute(impSolReq);
            }
            catch (Exception e)
            {
                ex = e;
                SendLine(container, "Error during import: {0}", ex.Message);
            }
            finally
            {
                result = ReadAndLogSolutionImportJobStatus(impSolReq.ImportJobId);
            }
            container.EndSection();
            return result;
        }

        private Version ExtractVersionFromSolutionZip(string filename)
        {
            container.StartSection("ExtractVersionFromSolutionZip");
            var solutionFilePath = Path.Combine(definitionpath, "solution.xml");
            using (var zip = ZipFile.OpenRead(filename))
            {
                zip.GetEntry("solution.xml").ExtractToFile(solutionFilePath, overwrite: true);
            }
            if (!File.Exists(solutionFilePath))
            {
                throw new FileNotFoundException($"Unable to unzip solution.xml from file: {filename}, invalid solution file.");
            }
            var xSolution = new XmlDocument();
            xSolution.Load(solutionFilePath);
            File.Delete(solutionFilePath);
            var xRoot = XML.FindChild(xSolution, "ImportExportXml");
            if (xRoot == null)
            {
                throw new XmlException("Cannot find root element ImportExportXml");
            }
            var xManifest = XML.FindChild(xRoot, "SolutionManifest");
            if (xManifest == null)
            {
                throw new XmlException("Cannot find element SolutionManifest");
            }
            var xVersion = XML.FindChild(xManifest, "Version");
            if (xVersion == null)
            {
                throw new XmlException("Cannot find element Version");
            }
            var version = new Version(xVersion.InnerText);
            container.Log("Version {0} extracted", version);
            container.EndSection();
            return version;
        }

        private string GetSolutionFilename(SolutionBlock block)
        {
            container.StartSection("GetSolutionFilename");
            var file = block.File;
            if (string.IsNullOrWhiteSpace(file))
            {
                file = block.Name;
            }
            var path = block.Path;
            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(definitionpath))
            {
                path = definitionpath;
            }
            path += path.EndsWith("\\") ? "" : "\\";
            string filename;
            if (block.Import.Type == SolutionTypes.Managed)
            {
                filename = path + file + "_managed.zip";
            }
            else if (block.Import.Type == SolutionTypes.Unmanaged)
            {
                filename = path + file + ".zip";
            }
            else
            {
                throw new ArgumentOutOfRangeException("Type", block.Import.Type, "Invalid Solution type");
            }

            if (filename.Contains("%"))
            {
                var envvars = Environment.GetEnvironmentVariables();
                foreach (DictionaryEntry de in envvars)
                {
                    filename = filename.Replace("%" + de.Key.ToString() + "%", de.Value.ToString());
                }
            }
            container.Log("Filename: {0}", filename);
            container.EndSection();
            return filename;
        }

        private ItemImportResult ImportSolutionBlock(SolutionBlock block)
        {
            container.StartSection("ImportSolutionBlock");
            var importResult = ItemImportResult.None;
            if (block.Import != null)
            {
                var name = block.Name;
                container.Log("Block: {0}", name);
                SendStatus(name, null);

                SendLine(container, "Importing solution: {0}", name);

                var filename = GetSolutionFilename(block);
                var version = ExtractVersionFromSolutionZip(filename);
                try
                {
                    ValidatePreReqs(container, block.Import, version);
                    var ImportCondition = CheckIfImportRequired(container, block.Import, name, version);
                    if (ImportCondition != SolutionImportConditions.Skip)
                    {
                        if (DoImportSolution(block.Import, filename, version))
                        {
                            if (ImportCondition == SolutionImportConditions.Create)
                            {
                                importResult = ItemImportResult.Created;
                            }
                            else
                            {
                                importResult = ItemImportResult.Updated;
                            }
                        }
                        else
                        {
                            importResult = ItemImportResult.Failed;
                            container.Log("Failed during import");
                        }
                        var publish = block.Import.PublishAll;
                        if (publish)
                        {
                            SendLine(container, "Publishing customizations");
                            container.Execute(new PublishAllXmlRequest());
                        }
                    }
                    else
                    {
                        importResult = ItemImportResult.Skipped;
                        container.Log("Skipped due to import condition");
                    }
                }
                catch (Exception ex)
                {
                    container.Log(ex);
                    importResult = ItemImportResult.Failed;
                    if (stoponerror)
                    {
                        throw;
                    }
                }
            }
            container.EndSection();
            return importResult;
        }

        private bool ReadAndLogSolutionImportJobStatus(Guid jobid)
        {
            container.StartSection("ReadAndLogSolutionImportJobStatus " + jobid);
            var success = false;
            var job = container.Retrieve("importjob", jobid, new ColumnSet("startedon", "completedon", "progress", "data"));
            if (job != null)
            {
                var name = "?";
                var result = "?";
                var err = "";
                var start = job.GetAttribute("startedon", DateTime.MinValue);
                var complete = job.GetAttribute("completedon", DateTime.MinValue);
                var time = complete != null && start != null ? complete.Subtract(start) : new TimeSpan();
                var prog = job.GetAttribute<double>("progress", 0);
                if (job.Contains("data", true))
                {
                    var doc = new XmlDocument();
                    var data = job.GetAttribute("data", "");
                    container.Log("Job data length: {0}", data.Length);
                    if (!string.IsNullOrWhiteSpace(data))
                    {
                        doc.LoadXml(data);
                        var namenode = doc.SelectSingleNode("//solutionManifest/UniqueName");
                        if (namenode != null) { name = namenode.InnerText; }
                        var resultnode = doc.SelectSingleNode("//solutionManifest/result/@result");
                        if (resultnode != null) { result = resultnode.Value; }
                        var errnode = doc.SelectSingleNode("//solutionManifest/result/@errortext");
                        if (errnode != null) { err = errnode.Value; }
                    }
                }
                if (prog >= 100 && result == "success")
                {
                    SendLine(container, "Solution {0} imported in {1}", name, time);
                    container.Log("Result: {0}\nError:  {1}\nTime:   {2}", result, err, time);
                    success = true;
                }
                else
                {
                    SendLine(container, "Solution: {0}", name);
                    SendLine(container, "Result:   {0}", result);
                    SendLine(container, "Error:    {0}", err);
                    SendLine(container, "Progress: {0}", prog);
                    SendLine(container, "Time:     {0}", time);
                }
            }
            container.Log("Returning: {0}", success);
            container.EndSection();
            return success;
        }

        private void ValidatePreReqs(IExecutionContainer container, SolutionBlockImport import, Version thisversion)
        {
            if (import.PreRequisites == null)
            {
                container.Log("No prereqs for solution import");
                return;
            }
            container.StartSection("ValidatePreReqs");
            var cSolutions = GetExistingSolutions(container);
            foreach (var prereq in import.PreRequisites)
            {
                var valid = false;
                var name = prereq.Name;
                var comparer = prereq.Comparer;
                var version = new Version();
                container.Log("Prereq: {0} {1} {2}", name, comparer, version);

                if (comparer == SolutionVersionComparers.eqthis || comparer == SolutionVersionComparers.gethis)
                {
                    version = thisversion;
                    comparer = comparer == SolutionVersionComparers.eqthis ? SolutionVersionComparers.eq : comparer == SolutionVersionComparers.gethis ? SolutionVersionComparers.ge : comparer;
                }
                else if (comparer != SolutionVersionComparers.any)
                {
                    version = new Version(prereq.Version.Replace('*', '0'));
                }

                foreach (var cdSolution in cSolutions.Entities)
                {
                    if (cdSolution.GetAttribute("uniquename", "") == name)
                    {
                        container.Log("Found matching solution");
                        switch (comparer)
                        {
                            case SolutionVersionComparers.any:
                                valid = true;
                                break;

                            case SolutionVersionComparers.eq:
                                valid = new Version(cdSolution.GetAttribute("version", "1.0.0.0")).Equals(version);
                                break;

                            case SolutionVersionComparers.ge:
                                valid = new Version(cdSolution.GetAttribute("version", "<undefined>")) >= version;
                                break;

                            default:
                                throw new ArgumentOutOfRangeException("Comparer", comparer, "Invalid comparer value");
                        }
                    }
                    if (valid)
                    {
                        break;
                    }
                }
                if (valid)
                {
                    SendLine(container, "Prerequisite {0} {1} {2} is satisfied", name, comparer, version);
                }
                else
                {
                    SendLine(container, "Prerequisite {0} {1} {2} is NOT satisfied", name, comparer, version);
                    throw new Exception("Prerequisite NOT satisfied (" + name + " " + comparer + " " + version + ")");
                }
            }
            container.EndSection();
        }
    }

    #endregion Private Methods
}