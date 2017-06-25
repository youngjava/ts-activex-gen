﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TsActivexGen.Util;
using Ookii.Dialogs;
using Forms = System.Windows.Forms;
using static System.IO.Path;
using static System.IO.File;
using static System.Windows.MessageBoxButton;
using static System.Windows.MessageBoxResult;
using System.Diagnostics;
using static System.Reflection.Assembly;
using TsActivexGen.ActiveX;
using System.Windows.Data;
using System.Collections.ObjectModel;
using static TsActivexGen.Wpf.Functions;
using System.IO;

namespace TsActivexGen.Wpf {
    public partial class MainWindow : Window {
        ObservableCollection<OutputFileDetails> fileList = new ObservableCollection<OutputFileDetails>();

        public MainWindow() {
            InitializeComponent();

            dgTypeLibs.ItemsSource = TypeLibDetails.FromRegistry.Value;

            txbFilter.TextChanged += (s, e) => applyFilter();

            dgTypeLibs.SelectionChanged += (s, e) => {
                if (e.AddedItems.Count == 0) { return; }
                addFiles();
            };

            var fileDlg = new VistaOpenFileDialog() {
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };
            btnBrowseTypeLibFile.Click += (s, e) => {
                if (!txbTypeLibFromFile.Text.IsNullOrEmpty()) { fileDlg.FileName = txbTypeLibFromFile.Text; }
                if (fileDlg.ShowDialog() == Forms.DialogResult.Cancel) { return; }
                txbTypeLibFromFile.Text = fileDlg.FileName;
                addFiles();
            };

            txbOutputFolder.Text = Combine(GetDirectoryName(GetEntryAssembly().Location), "typings");
            txbOutputFolder.TextChanged += (s, e) => ((IList<OutputFileDetails>)dtgFiles.ItemsSource).ForEach(x => x.OutputFolderRoot = txbOutputFolder.Text);
            btnBrowseOutputFolder.Click += (s, e) => fillFolder();

            dtgFiles.ItemsSource = fileList;

            btnOutput.Click += (s, e) => {
                var toOutput = dtgFiles.Items<OutputFileDetails>().Where(x => x.WriteOutput && !x.Name.IsNullOrEmpty()).ToList();
                if (toOutput.None()) { return; }

                Directory.CreateDirectory(txbOutputFolder.Text);

                string selectedPath;

                if (lbPackaging.SelectedValue<bool>()) {
                    //package for DefinitelyTyped
                    toOutput.ForEach(x=> {
                        //create subdirectory for all files
                        Directory.CreateDirectory(x.PackagedFolderPath);

                        //create tsconfig.json
                        x.WritePackageFile("tsconfig.json", GetTsConfig(x.FormattedName));

                        //create index.d.ts
                        var s1 = GetHeaders(x.Name,x.Description,x.LibraryUrl, txbAuthorName.Text, txbAuthorURL.Text);
                        s1 += Environment.NewLine;
                        s1 += ReferenceDirectives(x.Output.Dependencies);
                        s1 += x.Output.MainFile;
                        WriteAllText(x.PackagedFilePath, s1);

                        //create tests file
                        x.WriteTestsFile(x.Output.TestsFile);

                        //create tslint.json
                        x.WritePackageFile("tslint.json", @"{ ""extends"": ""dtslint / dt.json"" }");

                        //create package.json
                        x.WritePackageFile("package.json", @"{ ""dependencies"": { ""activex-helpers"": ""*""}}");
                    });
                    selectedPath = toOutput.First().PackagedFolderPath;
                } else {
                    //single file
                    toOutput.ForEach(x => WriteAllText(x.SingleFilePath, x.Output.MainFile));
                    selectedPath = toOutput.First().SingleFilePath;
                }

                var psi = new ProcessStartInfo("explorer.exe", $"/n /e,/select,\"{selectedPath}\"");
                Process.Start(psi);
            };

            btnClearFiles.Click += (s, e) => {
                tlbGenerator = new TlbInf32Generator();
                fileList.Clear();
            };
        }

        private bool createFile(string path) {
            if (!Exists(path)) { return true; }
            return MessageBox.Show($"Overwrite '{path}`?", "", YesNo) == Yes;
        }

        VistaFolderBrowserDialog folderDlg = new VistaFolderBrowserDialog() {
            ShowNewFolderButton = true
        };

        TlbInf32Generator tlbGenerator = new TlbInf32Generator();
        private void addFiles() {
            switch (cmbDefinitionType.SelectedIndex) {
                case 0:
                    var details = dgTypeLibs.SelectedItem<TypeLibDetails>();
                    tlbGenerator.AddFromRegistry(details.TypeLibID, details.MajorVersion, details.MinorVersion, details.LCID);
                    break;
                case 1:
                    tlbGenerator.AddFromFile(txbTypeLibFromFile.Text);
                    break;
                case 2:
                    break;
                default:
                    throw new InvalidOperationException();
            }

            fileList.Clear();
            new TSBuilder().GetTypescript(tlbGenerator.NSSet).SelectKVP((name, x) => new OutputFileDetails {
                Name = name,
                OutputFolderRoot = txbOutputFolder.Text,
                WriteOutput = true,
                Output = x
            }).AddRangeTo(fileList);
        }

        private bool fillFolder() {
            if (!txbOutputFolder.Text.IsNullOrEmpty()) { folderDlg.SelectedPath = txbOutputFolder.Text; }
            var result = folderDlg.ShowDialog();
            if (result == Forms.DialogResult.Cancel) { return false; }
            txbOutputFolder.Text = folderDlg.SelectedPath;
            return true;
        }

        private void applyFilter() {
            CollectionViewSource.GetDefaultView(dgTypeLibs.ItemsSource).Filter = x => (((TypeLibDetails)x).Name ?? "").Contains(txbFilter.Text, StringComparison.OrdinalIgnoreCase);
        }
    }
}