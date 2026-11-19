// SPDX-License-Identifier: AGPL-3.0-only
/**
* Digital Voice Modem - Desktop Dispatch Console
* AGPLv3 Open Source. Use is subject to license terms.
* DO NOT ALTER OR REMOVE COPYRIGHT NOTICES OR THIS FILE HEADER.
*
* @package DVM / Desktop Dispatch Console
* @license AGPLv3 License (https://opensource.org/licenses/AGPL-3.0)
*
*   Copyright (C) 2026 C. Lovell, Dev_Ranger
*
*/
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using System.Net;
using Markdig;

namespace dvmconsole
{
    public partial class DocumentationWindow : Window
    {
        private string docsRoot;

        public DocumentationWindow()
        {
            InitializeComponent();

            docsRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Docs");
            LoadDocumentationTree();
        }

        private void LoadDocumentationTree(string searchTerm = "")
        {
            treeDocs.Items.Clear();

            if (!Directory.Exists(docsRoot))
            {
                RenderHtml("<p>Documentation folder not found.</p>");
                return;
            }


            BuildTreeFromDirectory(docsRoot, treeDocs, searchTerm);

            ExpandAllTreeItems(treeDocs.Items);

            TreeViewItem firstDoc = FindFirstDocumentItem(treeDocs.Items);
            if (firstDoc != null)
            {
                firstDoc.IsSelected = true;
            }
            else
            {
                RenderHtml("<p>No matching documentation pages found.</p>");
            }
        }

        private void BuildTreeFromDirectory(string directoryPath, ItemsControl parent, string searchTerm = "")
        {
            var directoryEntries = Directory
                .GetFileSystemEntries(directoryPath)
                .Where(path =>
                    Directory.Exists(path) ||
                    Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase))
                .Select(path => new
                {
                    FullPath = path,
                    Name = Path.GetFileName(path),
                    IsDirectory = Directory.Exists(path),
                    SortPrefix = GetSortPrefix(Path.GetFileName(path)),
                    SortName = FormatDocTitle(Path.GetFileName(path))
                })
                .OrderBy(x => x.SortPrefix)
                .ThenBy(x => x.IsDirectory ? 1 : 0)
                .ThenBy(x => x.SortName, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in directoryEntries)
            {
                if (entry.IsDirectory)
                {
                    TreeViewItem folderItem = new TreeViewItem
                    {
                        Header = FormatDocTitle(entry.Name),
                        IsExpanded = true
                    };

                    BuildTreeFromDirectory(entry.FullPath, folderItem, searchTerm);

                    if (folderItem.Items.Count > 0)
                        parent.Items.Add(folderItem);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(searchTerm))
                    {
                        string fileText = File.ReadAllText(entry.FullPath);
                        string displayTitle = FormatDocTitle(entry.Name);

                        if (!displayTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) &&
                            !fileText.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    TreeViewItem fileItem = new TreeViewItem
                    {
                        Header = FormatDocTitle(entry.Name),
                        Tag = entry.FullPath
                    };

                    parent.Items.Add(fileItem);
                }
            }
        }

        private void ExpandAllTreeItems(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    item.IsExpanded = true;
                    ExpandAllTreeItems(item.Items);
                }
            }
        }

        private TreeViewItem FindFirstDocumentItem(ItemCollection items)
        {
            foreach (var obj in items)
            {
                if (obj is TreeViewItem item)
                {
                    if (item.Tag is string)
                        return item;

                    TreeViewItem childResult = FindFirstDocumentItem(item.Items);
                    if (childResult != null)
                        return childResult;
                }
            }

            return null;
        }

        private string FormatDocTitle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            value = Path.GetFileNameWithoutExtension(value);
            value = System.Text.RegularExpressions.Regex.Replace(value, @"^\d+\s*-\s*", "");
            return value.Trim();
        }

        private int GetSortPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return int.MaxValue;

            string name = Path.GetFileNameWithoutExtension(value);
            var match = System.Text.RegularExpressions.Regex.Match(name, @"^(\d+)\s*-\s*");

            if (match.Success && int.TryParse(match.Groups[1].Value, out int prefix))
                return prefix;

            return int.MaxValue;
        }
        private void treeDocs_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (treeDocs.SelectedItem is TreeViewItem item && item.Tag is string filePath && File.Exists(filePath))
                LoadDocument(filePath);
        }

        private void LoadDocument(string filePath)
        {
            try
            {
                string markdown = File.ReadAllText(filePath);
                RenderHtml(Markdown.ToHtml(markdown), filePath);
            }
            catch (IOException ex)
            {
                Log.WriteWarning($"Unable to read documentation: {ex.Message}");
                RenderHtml("<p>Unable to read this documentation page.</p>");
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.WriteWarning($"Unable to read documentation: {ex.Message}");
                RenderHtml("<p>Unable to read this documentation page.</p>");
            }
        }

        /// <summary>
        /// Resolves local Markdown navigation without opening files outside the documentation folder.
        /// </summary>
        internal static bool TryResolveDocumentPath(string root, Uri uri, out string filePath)
        {
            filePath = null;
            if (uri == null || !uri.IsAbsoluteUri || !uri.IsFile)
                return false;

            try
            {
                string rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(uri.LocalPath);
                if (!candidate.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ||
                    !Path.GetExtension(candidate).Equals(".md", StringComparison.OrdinalIgnoreCase))
                    return false;

                filePath = candidate;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
        }

        private void docViewer_Navigating(object sender, NavigatingCancelEventArgs e)
        {
            if (e.Uri == null || !e.Uri.IsAbsoluteUri || !e.Uri.IsFile)
                return;

            // NavigateToString has no file origin. Render linked Markdown ourselves,
            // after the browser finishes cancelling its raw-file navigation.
            e.Cancel = true;
            if (!TryResolveDocumentPath(docsRoot, e.Uri, out string filePath))
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsLoaded)
                    return;
                if (treeDocs.SelectedItem is TreeViewItem selected)
                    selected.IsSelected = false;
                LoadDocument(filePath);
            }));
        }

        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            LoadDocumentationTree(txtSearch.Text);
        }

        private void RenderHtml(string bodyHtml, string filePath = null)
        {
            bool darkMode = SettingsManager.Instance?.DarkMode ?? false;

            string backgroundColor = darkMode ? "#1e1e1e" : "#ffffff";
            string textColor = darkMode ? "#e6e6e6" : "#222222";
            string borderColor = darkMode ? "#3a3a3a" : "#dddddd";
            string codeBackground = darkMode ? "#2d2d2d" : "#f3f3f3";
            string linkColor = darkMode ? "#7db7ff" : "#0066cc";
            string baseElement = filePath == null ? string.Empty :
                $"<base href=\"{WebUtility.HtmlEncode(new Uri(Path.GetFullPath(filePath)).AbsoluteUri)}\">";

            string fullHtml = $@"
<html>
<head>
<meta charset='utf-8'>
{baseElement}
<style>
body {{
    font-family: Segoe UI, Arial, sans-serif;
    margin: 20px;
    font-size: 14px;
    line-height: 1.6;
    background: {backgroundColor};
    color: {textColor};
}}
h1, h2, h3, h4, h5, h6 {{
    color: {textColor};
    margin-top: 24px;
    margin-bottom: 12px;
}}
h1 {{ font-size: 28px; margin-top: 0; }}
h2 {{ font-size: 24px; }}
h3 {{ font-size: 20px; }}
p {{
    margin-top: 0;
    margin-bottom: 12px;
}}
ul, ol {{
    margin-top: 0;
    margin-bottom: 12px;
}}
code {{
    background: {codeBackground};
    padding: 2px 4px;
    border-radius: 4px;
    font-family: Consolas, monospace;
}}
pre {{
    background: {codeBackground};
    padding: 12px;
    border: 1px solid {borderColor};
    border-radius: 6px;
    overflow-x: auto;
    font-family: Consolas, monospace;
}}
pre code {{
    background: transparent;
    padding: 0;
    border-radius: 0;
}}
a {{
    color: {linkColor};
    text-decoration: none;
}}
a:hover {{
    text-decoration: underline;
}}
blockquote {{
    border-left: 4px solid {borderColor};
    margin: 12px 0;
    padding-left: 12px;
    color: {textColor};
}}
table {{
    border-collapse: collapse;
}}
th, td {{
    border: 1px solid {borderColor};
    padding: 6px 10px;
}}
</style>
</head>
<body>
{bodyHtml}
</body>
</html>";

            docViewer.NavigateToString(fullHtml);
        }
    }
}
