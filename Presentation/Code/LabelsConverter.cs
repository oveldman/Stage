using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using ViLabels.Backend;
using ViLabels.Objects;
using ViLabels.Objects.JsonSynchronize;
using ViLabels.Shared;

namespace ViLabels.JsonSynchronize
{
    public class LabelsConverter
    {
        private readonly FindLabel findLabel = new FindLabel();
        private readonly SaveLabels saveLabels = new SaveLabels();

        public void Convert()
        {
            ChangeAllLabels();
            if (AllDifferencesJson.AllMissingModules.Count != 0)
            {
                EditModules();
            }

            var projectConverter = new OldProjectConverter(CurrentProject.Path, CurrentProject.CurrentLanguage);
            projectConverter.Convert();
        }

        public void ConvertAlphabet()
        {
            this.findLabel.SetMetaInAllLanguage();
            foreach (var language in CurrentProject.AllLanguage)
            {
                foreach (var module in CurrentProject.AllModules)
                {
                    var pathModule = Path.Combine(CurrentProject.Path, language, string.Format(module + "{0}", ".json"));
                    var currentJObect = this.findLabel.ParseOneJsonFile(pathModule);
                    this.saveLabels.WriteLabelsToHardDrive(currentJObect, pathModule);
                }
            }

            this.findLabel.RemoveMetaInAllLanguage();
        }

        private void EditModules()
        {
            var allLanguages = CurrentProject.AllLanguage;
            var languages = new List<string>();
            var missingModules = AllDifferencesJson.AllMissingModules;

            var moduleName = missingModules[0].Name;
            foreach (var missingModule in missingModules)
            {
                if (missingModule.Language != "Meta" && missingModule.Language != "Meta\\Default")
                {
                    if (moduleName.Equals(missingModule.Name))
                    {
                        languages.Add(missingModule.Language);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (allLanguages.Count == languages.Count || missingModules[0].Delete)
            {
                RemoveModule(moduleName);

                if (missingModules[0].Delete)
                {
                    RemoveMissingModuleObject(missingModules[0]);
                }
                else
                {
                    missingModules.RemoveAt(0);
                    missingModules.RemoveAt(0);
                }
            }
            else
            {
                var differentLanguages = allLanguages.Except(languages).ToList();
                var language = differentLanguages[0];

                AddMissingModule(language);

                missingModules.Remove(missingModules[0]);
            }

            if (missingModules.Count != 0)
            {
                EditModules();
            }
        }

        private void RemoveMissingModuleObject(MissingModule deletableModule)
        {
            for (var i = 0; i < AllDifferencesJson.AllMissingModules.Count; i++)
            {
                if (AllDifferencesJson.AllMissingModules[i].Name.Equals(deletableModule.Name))
                {
                    AllDifferencesJson.AllMissingModules.RemoveAt(i);
                }
            }
        }

        private void AddMissingModule(string currentLanguage)
        {
            var path = CurrentProject.Path;
            var defaultPath = Path.Combine(
                CurrentProject.Path,
                "Meta\\Default",
                AllDifferencesJson.AllMissingModules[0].Name);

            if (!File.Exists(defaultPath))
            {
                CreateEmptyModule(currentLanguage, AllDifferencesJson.AllMissingModules[0].Name);
            }

            foreach (var module in AllDifferencesJson.AllMissingModules)
            {
                var language = module.Language;
                if (!language.Equals("Meta") && !language.Equals("Meta\\Default"))
                {
                    var languagePath = Path.Combine(path, language, module.Name);
                    var currentLanguagePath = Path.Combine(
                        path,
                        "Meta\\Default",
                        module.Name);

                    if (!File.Exists(languagePath) && File.Exists(currentLanguagePath))
                    {
                        File.Copy(currentLanguagePath, languagePath);
                    }
                }
            }
        }

        private void RemoveModule(string module)
        {
            this.findLabel.SetMetaInAllLanguage();
            foreach (var language in CurrentProject.AllLanguage)
            {
                var modulePath = Path.Combine(CurrentProject.Path, language, module);
                if (File.Exists(modulePath))
                {
                    File.Delete(modulePath);
                }
            }

            this.findLabel.RemoveMetaInAllLanguage();
        }

        private void CreateEmptyModule(string currentLanguage, string missingModule)
        {
            var languagePath = Path.Combine(CurrentProject.Path, "Meta\\Default", missingModule);
            var currentLanguagePath = Path.Combine(CurrentProject.Path, currentLanguage, missingModule);

            File.Copy(currentLanguagePath, languagePath);
        }

        private void ChangeAllLabels()
        {
            foreach (var missingLabel in AllDifferencesJson.AllMissingLabels)
            {
                if (missingLabel.Sync)
                {
                    AddAllLabels(missingLabel);
                }

                if (missingLabel.Delete)
                {
                    RemoveLabel(missingLabel);
                }
            }
        }

        private void AddAllLabels(MissingLabel changeLabel)
        {
            var filePath = Path.Combine(CurrentProject.Path, changeLabel.Language, changeLabel.Module);
            var jsonModule = this.findLabel.ParseOneJsonFile(filePath);
            var getInChildModule = jsonModule;
            var childPath = changeLabel.LabelPath.Split('.');
            for (var i = 0; i < childPath.Length; i++)
            {
                if ((childPath.Length - 1) != i)
                {
                    var canDeeper = this.findLabel.TryGoDeeper(getInChildModule, childPath[i]);
                    if (canDeeper)
                    {
                        getInChildModule = this.findLabel.GetDeeperChild(getInChildModule, childPath[i]);
                    }
                    else
                    {
                        getInChildModule = AddGroup(getInChildModule, childPath[i]);
                        getInChildModule = this.findLabel.GetDeeperChild(getInChildModule, childPath[i]);
                    }
                }
                else
                {
                    if (changeLabel.Sync)
                    {
                        var isMarkdown = CheckOnMarkdown(changeLabel.Module, changeLabel.LabelPath);
                        getInChildModule = AddLabel(getInChildModule, childPath[i], changeLabel.Language, isMarkdown);
                    }
                }
            }

            this.saveLabels.WriteLabelsToHardDrive(jsonModule, filePath);
        }

        private bool CheckOnMarkdown(string module, string path)
        {
            var filePath = Path.Combine(CurrentProject.Path, "Meta", "Default", module);
            var defaultModule = this.findLabel.ParseOneJsonFile(filePath);
            var pathSplit = path.Split('.');

            foreach (string onePath in pathSplit)
            {
                var canDeeper = this.findLabel.TryGoDeeper(defaultModule, onePath);
                if (canDeeper)
                {
                    defaultModule = this.findLabel.GetDeeperChild(defaultModule, onePath);
                }
            }

            var markdownValue = defaultModule.SelectToken("isMarkdown");
            var isMarkdown = markdownValue.Value<bool>();
            return isMarkdown;
        }

        private JObject AddGroup(JObject jsonModule, string groupName)
        {
            var newGroupTranslate = JObject.Parse("{}");
            jsonModule.Add(groupName, newGroupTranslate);
            return jsonModule;
        }

        private JObject AddLabel(JObject jsonModule, string labelName, string language, bool isNewMarkdown)
        {
            if (language.Equals("Meta"))
            {
                var todayDate = this.findLabel.GetDateToday();
                var setNewMetaLabel =
                    JObject.FromObject(new { description = "No description", adddate = todayDate, editdate = todayDate });
                jsonModule.Add(labelName, setNewMetaLabel);
            }
            else
            {
                if (jsonModule.SelectToken("value") == null && jsonModule.SelectToken("isMarkdown") == null)
                {
                    var translation = string.Empty;
                    jsonModule.Add(
                        labelName,
                        JToken.FromObject(new { value = translation, isMarkdown = isNewMarkdown }));
                }
            }

            return jsonModule;
        }

        private void RemoveLabel(MissingLabel changeLabel)
        {
            this.findLabel.SetMetaInAllLanguage();
            foreach (var language in CurrentProject.AllLanguage)
            {
                var modulePath = Path.Combine(CurrentProject.Path, language, changeLabel.Module);
                JObject jsonModule;
                try
                {
                    jsonModule = this.findLabel.ParseOneJsonFile(modulePath);
                }
                catch (Exception)
                {
                    continue;
                }

                var getInChildModule = jsonModule;

                var childPath = changeLabel.LabelPath.Split('.');
                for (var i = 0; i < childPath.Length; i++)
                {
                    var canDeeper = this.findLabel.TryGoDeeper(getInChildModule, childPath[i]);
                    if (canDeeper && childPath.Length - 1 != i)
                    {
                        getInChildModule = this.findLabel.GetDeeperChild(getInChildModule, childPath[i]);
                    }
                    else
                    {
                        if (childPath.Length - 1 != i)
                        {
                            break;
                        }
                    }

                    if (i == childPath.Length - 1)
                    {
                        getInChildModule.Remove(childPath[i]);
                    }
                }

                this.saveLabels.WriteLabelsToHardDrive(jsonModule, modulePath);
            }

            this.findLabel.RemoveMetaInAllLanguage();
        }
    }
}
