using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;

using ViLabels.Backend;
using ViLabels.Objects;
using ViLabels.Objects.JsonSynchronize;
using ViLabels.Shared;
using ViLabels.Validation.JsonSynchronize;

namespace ViLabels.JsonSynchronize
{
    public class JsonCheck
    {
        private readonly JsonCompare jsonCompare = new JsonCompare();
        private readonly LabelsConverter labelConverter = new LabelsConverter();
        private readonly FindLabel findLabel = new FindLabel();
        private readonly IFileSystem fileSystem;

        public JsonCheck()
        {
            this.fileSystem = new FileSystem();
        }

        public JsonCheck(IFileSystem newFileSystem)
        {
            this.fileSystem = newFileSystem;
            this.findLabel = new FindLabel(newFileSystem);
        }

        public bool CheckProject()
        {
            AllDifferencesJson.HumanError = false;
            AllDifferencesJson.MakeEmpty();
            if (CheckModuleHumanErrors())
            {
                AllDifferencesJson.HumanError = true;
                return false;
            }

            if (!this.jsonCompare.Compare())
            {
                AllDifferencesJson.HumanError = false;
                return false;
            }

            return true;
        }

        public void ConvertLabels()
        {
            this.labelConverter.Convert();
        }

        public bool CheckModuleHumanErrors()
        {
            var hasErrors = false;
            var jsonValidator = new JsonValidator();
            this.findLabel.SetMetaInAllLanguage();
            foreach (var language in CurrentProject.AllLanguage)
            {
                var newJsonLanguage = this.findLabel.GetAllFilesOneLanguage(language);

                foreach (var module in newJsonLanguage.AllJsonFileNames)
                {
                    var viLabels = new VILabelsJObject(module, language, this.fileSystem);
                    var resultJson = jsonValidator.Validate(viLabels);
                    if (!resultJson.IsValid)
                    {
                        hasErrors = true;
                        var path = Path.Combine(CurrentProject.Path, language, module);
                        var newMissingModule = new MissingModule
                        {
                            Language = language,
                            Name = module,
                            ErrorType = "HumanError",
                            Action = "Click here to go to the File",
                            Path = path,
                            ErrorMessage = resultJson.BrokenRules[0].Message
                        };
                        AllDifferencesJson.AllMissingModules.Add(newMissingModule);
                    }
                }
            }

            this.findLabel.RemoveMetaInAllLanguage();

            return hasErrors;
        }

        public List<int> CheckEmptyTranslations()
        {
            var emptyTranslations = new List<int>();
            foreach (var oneLanguage in CurrentProject.AllLanguage)
            {
                var allJson = this.findLabel.GetAllFilesOneLanguage(oneLanguage);
                foreach (var oneModule in CurrentProject.AllModules)
                {
                    var jsonModule = this.findLabel.ParseOneJsonFile(Path.Combine(CurrentProject.Path, oneLanguage, oneModule + ".json"));
                    allJson.Json.Add(jsonModule);
                }

                CurrentProject.SetAllLabels(allJson.Json);
                var loadLabels = new LoadLabels();
                CurrentProject.SetAllChilds(loadLabels.GetLabels());
                var empty = CountEmptyTranslation();
                emptyTranslations.Add(empty);
            }

            return emptyTranslations;
        }

        private int CountEmptyTranslation()
        {
            var countEmptyTranslations = 0;
            foreach (var oneChild in CurrentProject.AllChildsList)
            {
                if (!oneChild.OnlyFamily)
                {
                    var hoi = oneChild.ChildLabel.First.SelectToken("value").ToString();
                    if (hoi.Equals(string.Empty))
                    {
                        countEmptyTranslations++;
                    }
                }
            }

            return countEmptyTranslations;
        }
    }
}
