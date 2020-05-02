// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using Roslynator.Migration;
using static Roslynator.Logger;

namespace Roslynator.CommandLine
{
    internal class MigrateCommand
    {
        private static readonly Regex _versionRegex = new Regex(@"\A(?<version>\d+\.\d+\.\d+)(?<suffix>(-.*)?)\z");

        public MigrateCommand(ImmutableArray<string> paths, string identifier, Version version, bool dryRun)
        {
            Paths = paths;
            Identifier = identifier;
            Version = version;
            DryRun = dryRun;
        }

        public ImmutableArray<string> Paths { get; }

        public string Identifier { get; }

        public Version Version { get; }

        public bool DryRun { get; }

        public CommandResult Execute()
        {
            try
            {
                var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                CancellationToken cancellationToken = cts.Token;

                try
                {
                    return Execute(cancellationToken);
                }
                catch (OperationCanceledException ex)
                {
                    OperationCanceled(ex);
                }
                catch (AggregateException ex)
                {
                    OperationCanceledException operationCanceledException = ex.GetOperationCanceledException();

                    if (operationCanceledException != null)
                    {
                        OperationCanceled(operationCanceledException);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            finally
            {
            }

            return CommandResult.Canceled;
        }

        public CommandResult Execute(CancellationToken cancellationToken)
        {
            var result = CommandResult.Success;

            foreach (string path in Paths)
            {
                CommandResult result2 = ExecutePath(path, cancellationToken);

                if (result2 == CommandResult.Canceled)
                    return CommandResult.Canceled;

                if (result != CommandResult.Fail
                    && result != CommandResult.Success)
                {
                    result = result2;
                }
            }

            return CommandResult.Success;
        }

        private CommandResult ExecutePath(string path, CancellationToken cancellationToken)
        {
            if (Directory.Exists(path))
            {
                WriteLine($"Search '{path}'", Verbosity.Detailed);
                return ExecuteDirectory(path, cancellationToken);
            }
            else if (File.Exists(path))
            {
                WriteLine($"Search '{path}'", Verbosity.Detailed);
                return ExecuteFile(path);
            }
            else
            {
                WriteLine($"File or directory not found: '{path}'", Colors.Message_Warning, Verbosity.Minimal);
            }

            return CommandResult.None;
        }

        private CommandResult ExecuteDirectory(string directoryPath, CancellationToken cancellationToken)
        {
            var result = CommandResult.None;

#if NETCOREAPP3_1
            var enumerationOptions = new EnumerationOptions() { IgnoreInaccessible = true, RecurseSubdirectories = true };

            IEnumerable<string> files = Directory.EnumerateFiles(directoryPath, "*.*", enumerationOptions);
#else
            IEnumerable<string> files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories);
#endif

            foreach (string filePath in files)
            {
                CommandResult result2 = ExecuteFile(filePath);

                if (result2 == CommandResult.Canceled)
                    return CommandResult.Canceled;

                if (result != CommandResult.Fail
                    && result != CommandResult.Success)
                {
                    result = result2;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return result;
        }

        private CommandResult ExecuteFile(string path)
        {
            string extension = Path.GetExtension(path);

            if (string.Equals(extension, ".csproj", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".props", StringComparison.OrdinalIgnoreCase))
            {
                if (!GeneratedCodeUtility.IsGeneratedCodeFile(path))
                    return ExecuteProject(path);
            }
            else if (string.Equals(extension, ".ruleset", StringComparison.OrdinalIgnoreCase))
            {
                if (!GeneratedCodeUtility.IsGeneratedCodeFile(path))
                    return ExecuteRuleSet(path);
            }

            WriteLine(path, Verbosity.Diagnostic);
            return CommandResult.None;
        }

        private CommandResult ExecuteProject(string path)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(path, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
            }
            catch (XmlException ex)
            {
                WriteLine($"Cannot load '{path}'", Verbosity.Minimal);
                WriteError(ex, verbosity: Verbosity.Minimal);
                return CommandResult.None;
            }

            XElement root = document.Root;

            if (root.Attribute("Sdk")?.Value == "Microsoft.NET.Sdk")
            {
                WriteLine($"Analyze '{path}'", Verbosity.Detailed);
                return ExecuteProject(path, document);
            }
            else
            {
                //TODO: Migrate old-style project
                WriteLine($"Project does not support migration: '{path}'", Colors.Message_Warning, Verbosity.Detailed);

                return CommandResult.None;
            }
        }

        private CommandResult ExecuteProject(string path, XDocument document)
        {
            List<LogMessage> messages = null;

            foreach (XElement itemGroup in document.Root.Descendants("ItemGroup"))
            {
                XElement analyzers = null;
                XElement formattingAnalyzers = null;

                foreach (XElement e in itemGroup.Elements("PackageReference"))
                {
                    string packageId = e.Attribute("Include")?.Value;

                    if (packageId == null)
                        continue;

                    if (packageId == "Roslynator.Formatting.Analyzers")
                        formattingAnalyzers = e;

                    if (packageId == "Roslynator.Analyzers")
                        analyzers = e;
                }

                if (analyzers == null)
                    continue;

                if (formattingAnalyzers != null)
                {
                    string versionText = formattingAnalyzers.Attribute("Version")?.Value;

                    if (versionText == null)
                    {
                        WriteXmlError(formattingAnalyzers, "Version attribute not found");
                        continue;
                    }

                    if (versionText != null)
                    {
                        Match match = _versionRegex.Match(versionText);

                        if (match?.Success != true)
                        {
                            WriteXmlError(formattingAnalyzers, $"Invalid version '{versionText}'");
                            continue;
                        }

                        versionText = match.Groups["version"].Value;

                        string suffix = match.Groups["suffix"]?.Value;

                        if (!Version.TryParse(versionText, out Version version))
                        {
                            WriteXmlError(formattingAnalyzers, $"Invalid version '{versionText}'");
                            continue;
                        }

                        if (version > Versions.Version_1_0_0
                            || suffix == null)
                        {
                            continue;
                        }
                    }
                }

                if (formattingAnalyzers != null)
                {
                    var message = new LogMessage("Update package 'Roslynator.Formatting.Analyzers' to '1.0.0'", Colors.Message_OK, Verbosity.Normal);

                    (messages ?? ( messages = new List<LogMessage>())).Add(message);

                    formattingAnalyzers.SetAttributeValue("Version", "1.0.0");
                }
                else
                {
                    var message = new LogMessage("Add package 'Roslynator.Formatting.Analyzers 1.0.0'", Colors.Message_OK, Verbosity.Normal);

                    (messages ?? (messages = new List<LogMessage>())).Add(message);

                    XText whitespace = null;

                    if (analyzers.NodesBeforeSelf().LastOrDefault() is XText xtext
                        && xtext != null
                        && string.IsNullOrWhiteSpace(xtext.Value))
                    {
                        whitespace = xtext;
                    }

                    analyzers.AddAfterSelf(whitespace, new XElement("PackageReference", new XAttribute("Include", "Roslynator.Formatting.Analyzers"), new XAttribute("Version", "1.0.0")));
                }
            }

            if (messages != null)
            {
                WriteUpdateMessages(path, messages);

                if (!DryRun)
                {
                    var settings = new XmlWriterSettings() { OmitXmlDeclaration = true };

                    using (XmlWriter xmlWriter = XmlWriter.Create(path, settings))
                        document.Save(xmlWriter);
                }

                return CommandResult.Success;
            }
            else
            {
                return CommandResult.None;
            }
        }

        private CommandResult ExecuteRuleSet(string path)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (XmlException ex)
            {
                WriteLine($"Cannot load '{path}'", Verbosity.Minimal);
                WriteError(ex, verbosity: Verbosity.Minimal);
                return CommandResult.None;
            }

            WriteLine($"Analyze '{path}'", Verbosity.Detailed);

            var ids = new Dictionary<string, XElement>();

            foreach (XElement element in document.Root.Elements("Rules").Elements("Rule"))
            {
                string id = element.Attribute("Id")?.Value;

                if (id != null)
                    ids[id] = element;
            }

            XElement analyzers = document.Root.Elements("Rules").LastOrDefault(f => f.Attribute("AnalyzerId")?.Value == "Roslynator.CSharp.Analyzers");

            if (analyzers == null)
                return CommandResult.None;

            XElement formattingAnalyzers = document.Root.Elements("Rules").FirstOrDefault(f => f.Attribute("AnalyzerId")?.Value == "Roslynator.Formatting.Analyzers");

            if (formattingAnalyzers == null)
            {
                formattingAnalyzers = new XElement(
                    "Rules",
                    new XAttribute("AnalyzerId", "Roslynator.Formatting.Analyzers"),
                    new XAttribute("RuleNamespace", "Roslynator.Formatting.Analyzers"));

                analyzers.AddAfterSelf(formattingAnalyzers);
            }

            List<LogMessage> messages = null;

            foreach (KeyValuePair<string, XElement> kvp in ids)
            {
                if (!AnalyzersMapping.Mapping.TryGetValue(kvp.Key, out ImmutableArray<string> newIds))
                    continue;

                foreach (string newId in newIds)
                {
                    if (ids.ContainsKey(newId))
                        continue;

                    string action = kvp.Value.Attribute("Action")?.Value ?? "Info";
                    var newRule = new XElement(
                        "Rule",
                        new XAttribute("Id", newId),
                        new XAttribute("Action", action));

                    var message = new LogMessage($"Update rule '{kvp.Key}' to '{newId}' ({action})", Colors.Message_OK, Verbosity.Normal);

                    (messages ?? (messages = new List<LogMessage>())).Add(message);

                    formattingAnalyzers.Add(newRule);

                    if (kvp.Value.Parent != null)
                        kvp.Value.Remove();
                }
            }

            if (messages != null)
            {
                WriteUpdateMessages(path, messages);

                if (!DryRun)
                {
                    if (analyzers.IsEmpty)
                        analyzers.Remove();

                    var settings = new XmlWriterSettings() { OmitXmlDeclaration = false, Indent = true };

                    using (XmlWriter xmlWriter = XmlWriter.Create(path, settings))
                    {
                        document.Save(xmlWriter);
                    }
                }

                return CommandResult.Success;
            }

            return CommandResult.None;
        }

        private static void WriteXmlError(XElement element, string message)
        {
            WriteLine($"{message}, line: {((IXmlLineInfo)element).LineNumber}, file: '{element}'", Colors.Message_Warning, Verbosity.Detailed);
        }

        private static void WriteUpdateMessages(string path, List<LogMessage> messages)
        {
            WriteLine($"Update '{path}'", Colors.Message_OK, Verbosity.Minimal);

            foreach (LogMessage update in messages)
            {
                Write("  ", update.Verbosity);
                WriteLine(update);
            }
        }

        protected virtual void OperationCanceled(OperationCanceledException ex)
        {
            WriteLine("Operation was canceled.", Verbosity.Quiet);
        }
    }
}
