using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ViLabels.Objects;
using ViLabels.Objects.JsonSynchronize;
using ViLabels.Shared;

namespace ViLabels.JsonSynchronize
{
    public class JsonCompare
    {
        private readonly List<string> allLanguages = CurrentProject.AllLanguage;
        private readonly List<MissingModule> allMissingModules = new List<MissingModule>();
        private readonly List<MissingLabel> allMissingLabels = new List<MissingLabel>();
        private readonly IFileSystem fileSystem;
        private readonly FindLabel findLabel = new FindLabel();
        private List<AllJsonInOneLanguage> allJsonAllLanguages = new List<AllJsonInOneLanguage>();
        private List<string> allCurrentModuleLabelPath = new List<string>();

        public JsonCompare()
        {
                this.fileSystem = new FileSystem();
        }

        public JsonCompare(IFileSystem newFileSystem)
        {
            this.fileSystem = newFileSystem;
            this.findLabel = new FindLabel(this.fileSystem);
        }

        public bool Compare()
        {
            CompareFileNames();
            CompareLabelNames();

            if (this.allMissingModules.Count == 0 && this.allMissingLabels.Count == 0)
            {
                return true;
            }

            AllDifferencesJson.AllMissingModules = this.allMissingModules;
            AllDifferencesJson.AllMissingLabels = this.allMissingLabels;
            return false;
        }

        private void GetFileNames()
        {
            this.allJsonAllLanguages = new List<AllJsonInOneLanguage>();
            this.findLabel.SetMetaInAllLanguage();
            foreach (string language in this.allLanguages)
            {
                var newJsonLanguage = this.findLabel.GetAllFilesOneLanguage(language);
                this.allJsonAllLanguages.Add(newJsonLanguage);
            }

            this.findLabel.RemoveMetaInAllLanguage();
        }

        private void CompareFileNames()
        {
            GetFileNames();

            foreach (var jsonLanguage in this.allJsonAllLanguages)
            {
                foreach (var jsonModule in jsonLanguage.AllJsonFileNames)
                {
                    FindMissingJsonFile(jsonLanguage.Language, jsonModule);
                }

                if (jsonLanguage.AllJsonFileNames.Count == 0)
                {
                    AddAllModules(jsonLanguage);
                }
            }

            GetOnlyDefaultModules();
        }

        private void CompareLabelNames()
        {
            ParseLabels();
            LoopLabels();
            FindMissingLabels();
        }

        private void FindMissingJsonFile(string language, string module)
        {
            foreach (var jsonLanguage in this.allJsonAllLanguages)
            {
                if (!jsonLanguage.Language.Equals(language))
                {
                    for (int i = 0; i < jsonLanguage.AllJsonFileNames.Count; i++)
                    {
                        if (jsonLanguage.AllJsonFileNames[i].Equals(module))
                        {
                            break;
                        }

                        if (i == (jsonLanguage.AllJsonFileNames.Count - 1))
                        {
                            AddMissingModule(jsonLanguage, module);
                        }
                    }
                }
            }
        }

        private void AddAllModules(AllJsonInOneLanguage allJsons)
        {
            foreach (var jsonLanguage in this.allJsonAllLanguages)
            {
                foreach (string jsonFileName in jsonLanguage.AllJsonFileNames)
                {
                    AddMissingModule(allJsons, jsonFileName);
                }
            }
        }

        private void AddMissingModule(AllJsonInOneLanguage jsonLanguage, string module)
        {
                MissingModule newMissingModule = new MissingModule
                                                     {
                                                         Language = jsonLanguage.Language,
                                                         Name = module,
                                                         ErrorType = "Missing Subject",
                                                         Action = "Add missing subject"
                                                     };
                if (FindDuplucateMissingModule(newMissingModule))
                {
                    this.allMissingModules.Add(newMissingModule);
                }
        }

        private void GetOnlyDefaultModules()
        {
            var missingModules = this.allMissingModules;
            var duplicateModulesInLanguages = missingModules.GroupBy(x => x.Name).Where(group => group.Count() == CurrentProject.AllLanguage.Count).Select(group => group.Key);

            var modulesInLanguages = duplicateModulesInLanguages as IList<string> ?? duplicateModulesInLanguages.ToList();
            foreach (var missingModule in this.allMissingModules)
            {
                foreach (var duplicateModule in modulesInLanguages)
                {
                    if (missingModule.Name.Equals(duplicateModule))
                    {
                        missingModule.ErrorType = "Only in default";
                        missingModule.Action = "Delete unused subject";
                    }
                }
            }
        }

        private bool FindDuplucateMissingModule(MissingModule newMissingModule)
        {
            foreach (var missingModule in this.allMissingModules)
            {
                if (missingModule.Name.Equals(newMissingModule.Name) && missingModule.Language.Equals(newMissingModule.Language))
                {
                    return false;
                }
            }

            return true;
        }

        private void ParseLabels()
        {
            foreach (var allJsonOneModule in this.allJsonAllLanguages)
            {
                foreach (var oneModule in allJsonOneModule.AllJsonFileNames)
                {
                    string pathModule = Path.Combine(CurrentProject.Path, allJsonOneModule.Language, oneModule);
                    StreamReader stringJsonFileReader = this.fileSystem.File.OpenText(pathModule);
                    string stringJsonFile = stringJsonFileReader.ReadToEnd();
                    stringJsonFileReader.Close();
                    JObject jsonModule = DeserializeJson(stringJsonFile);
                    allJsonOneModule.Json.Add(jsonModule);
                }
            }
        }

        private JObject DeserializeJson(string stringJsonFile)
        {
            return JsonConvert.DeserializeObject<JObject>(stringJsonFile);
        }

        private void LoopLabels()
        {
            foreach (var allLabels in this.allJsonAllLanguages)
            {
                foreach (JObject oneJObjectLabel in allLabels.Json)
                {
                    this.allCurrentModuleLabelPath = new List<string>();
                    foreach (JProperty label in oneJObjectLabel.Properties())
                    {
                        GoChildLabels(label, allLabels.Language.Equals("Meta"));
                    }

                    allLabels.AllLabelPath.Add(this.allCurrentModuleLabelPath);
                }
            }
        }

        private void GoChildLabels(JProperty childLabels, bool isMeta)
        {
            bool proceedChildAproved = false;

            if (isMeta)
            {
                if (childLabels.Value.Type == JTokenType.Object && !GetMetaLabels(childLabels.Value))
                {
                    proceedChildAproved = true;
                }
            }
            else
            {
                if (childLabels.Value.Type == JTokenType.Object && !GetLabels(childLabels.Value))
                {
                    proceedChildAproved = true;
                }
            }

            if (proceedChildAproved)
            {
                foreach (JProperty child in ((JObject)childLabels.Value).Properties())
                {
                    GoChildLabels(child, isMeta);
                }
            }
            else
            {
                this.allCurrentModuleLabelPath.Add(childLabels.Path);
            }
        }

        private bool GetMetaLabels(JToken metaLabel)
        {
            JToken description = metaLabel.SelectToken("description");
            JToken addDate = metaLabel.SelectToken("adddate");
            JToken editDate = metaLabel.SelectToken("editdate");
            if (description != null && addDate != null && editDate != null)
            {
                return true;
            }

            return false;
        }

        private bool GetLabels(JToken label)
        {
            JToken value = label.SelectToken("value");
            JToken isMarkdown = label.SelectToken("isMarkdown");
            if (value != null && isMarkdown != null)
            {
                return true;
            }

            return false;
        }

        private void FindMissingLabels()
        {
            foreach (var oneLanguage in this.allJsonAllLanguages)
            {
                for (int i = 0; i < oneLanguage.AllLabelPath.Count; i++)
                {
                    foreach (var onePath in oneLanguage.AllLabelPath[i])
                    {
                        CompareMissingLabel(onePath, oneLanguage.Language, oneLanguage.AllJsonFileNames[i]);
                    }
                }
            }
        }

        private void CompareMissingLabel(string labelPath, string language, string module)
        {
            foreach (var oneOtherLanguage in this.allJsonAllLanguages)
            {
                if (!oneOtherLanguage.Language.Equals(language))
                {
                    for (var y = 0; y < oneOtherLanguage.AllJsonFileNames.Count; y++)
                    {
                        if (oneOtherLanguage.AllJsonFileNames[y].Equals(module))
                        { 
                            for (int i = 0; i < oneOtherLanguage.AllLabelPath[y].Count; i++)
                            {
                                if (oneOtherLanguage.AllLabelPath[y][i].Equals(labelPath))
                                {
                                    break;
                                }

                                if (i == (oneOtherLanguage.AllLabelPath[y].Count - 1))
                                {
                                    MissingLabel missingLabel = new MissingLabel
                                                                    {
                                                                        Language = oneOtherLanguage.Language,
                                                                        Module = module,
                                                                        LabelPath = labelPath
                                                                    };
                                    if (FindDuplucateMissingLabel(missingLabel))
                                    { 
                                        this.allMissingLabels.Add(missingLabel);
                                    }
                                }
                        }
                    }
                }
                }
            }
        }

        private bool FindDuplucateMissingLabel(MissingLabel newMissingLabel)
        {
            foreach (var missingLabel in this.allMissingLabels)
            {
                if (missingLabel.Language.Equals(newMissingLabel.Language) && missingLabel.Module.Equals(newMissingLabel.Module) && missingLabel.LabelPath.Equals(newMissingLabel.LabelPath))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
