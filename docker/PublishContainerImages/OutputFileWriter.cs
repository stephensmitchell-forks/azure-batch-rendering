﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using PublishContainerImages.Model;

namespace PublishContainerImages
{
    class OutputFileWriter
    {
        private const string RelativePathToTestsDir = "OutputTests";
        private const string RelativePathToImageTxtDir = "OutputImages";

        public static void _outputLatestImagesFile(List<string> latestImages)
        {
            PublishContainerImages.WriteLog($"\nWriting the following entries to {PublishContainerImages.LatestImagesFilename}:"); 
            latestImages.ForEach(PublishContainerImages.WriteLog);
            var builtImagesFilePath = Path.Combine(OutputImagesTxtFilePath(), PublishContainerImages.LatestImagesFilename);
            Directory.CreateDirectory(new FileInfo(builtImagesFilePath).DirectoryName);
            File.WriteAllLines(builtImagesFilePath, latestImages);
        }

        public static void _outputTaggedImagesFile(List<string> taggedImages)
        {
            PublishContainerImages.WriteLog($"\nWriting the following entries to {PublishContainerImages.TaggedImagesFilename}:");
            taggedImages.ForEach(PublishContainerImages.WriteLog);
            var builtImagesFilePath = Path.Combine(OutputImagesTxtFilePath(), PublishContainerImages.TaggedImagesFilename);
            Directory.CreateDirectory(new FileInfo(builtImagesFilePath).DirectoryName);
            File.WriteAllLines(builtImagesFilePath, taggedImages);
        }

        public static void _outputContainerImageMetadataFile(List<ContainerImageDefinition> builtImages, bool outputFullMetadataFile)
        {
            PublishContainerImages.WriteLog($"\nWriting the following entries to {PublishContainerImages.BuiltImageMetadataFilename}:");
            builtImages.Select(x => x.ContainerImage).ToList().ForEach(PublishContainerImages.WriteLog);

            var builtImagesFilePath = Path.Combine(OutputImagesTxtFilePath(), PublishContainerImages.BuiltImageMetadataFilename);
            Directory.CreateDirectory(new FileInfo(builtImagesFilePath).DirectoryName);

            var imagesWithMetadata = builtImages.Where(im => im.Metadata != null).ToList();

            dynamic metaDataOutput = new ExpandoObject();
            if (outputFullMetadataFile)
            {
                metaDataOutput.imageReferences = ImageReference.GetAllImageReferences();
            }

            metaDataOutput.containerImages = new dynamic[imagesWithMetadata.Count];

            int index = 0;
            foreach (var image in imagesWithMetadata)
            {
                dynamic imageEntry = new ExpandoObject();

                imageEntry.containerImage = $"{image.ContainerImage}:latest";
                imageEntry.os = image.Metadata.Os;
                imageEntry.app = image.Metadata.App;
                imageEntry.appVersion = image.Metadata.AppVersion;
                imageEntry.renderer = image.Metadata.Renderer;
                imageEntry.rendererVersion = image.Metadata.RendererVersion;
                imageEntry.imageReferenceId = image.Metadata.ImageReferenceId;

                metaDataOutput.containerImages[index++] = imageEntry;
            }

            string metadataJson = JsonSerializeObject(metaDataOutput);
            File.WriteAllText(builtImagesFilePath, metadataJson);
            PublishContainerImages.WriteLog($"\nWrote metadata file at: {builtImagesFilePath}, file contents:");
            PublishContainerImages.WriteLog(metadataJson);
        }

        public static void _outputTestFiles(List<ContainerImagePayload> payloads, List<string> builtImages)
        {
            PublishContainerImages.WriteLog($"\nWriting test files for the following images:\n { string.Join("\n", builtImages)}");

            var payloadsWithTests = _removeInvalidPayloads(payloads);
            var payloadsWithTestsAndImagesTagged = _updateTestConfigAndParametersWithTaggedImage(payloadsWithTests, builtImages);

            payloadsWithTestsAndImagesTagged.ForEach(payload =>
            {
                var parametersPath = Path.Combine(OutputTestPath(), payload.TestConfigurationDefinition.Parameters);
                var parametersJson = JsonSerializeObject(payload.TestParametersDefinition);
                FileInfo paramsFile = new FileInfo(parametersPath);
                Directory.CreateDirectory(paramsFile.DirectoryName);
                File.WriteAllText(parametersPath, parametersJson);
                PublishContainerImages.WriteLog($"\nWrote parameters file at: {paramsFile.FullName}, file contents:");
                PublishContainerImages.WriteLog(parametersJson);
            });

            var testsConfiguration = new TestsDefinition
            {
                Tests = payloadsWithTestsAndImagesTagged.Select(payload =>
                {
                    var config = payload.TestConfigurationDefinition;
                    config.Parameters = Path.Combine(OutputTestPath(), config.Parameters).Replace("\\", "/");
                    return config;
                }).ToArray(),
                Images = new[]
                {
                    new TestMarketplaceImageDefinition
                    {
                        Offer = "microsoft-azure-batch",
                        OsType = "linux",
                        Publisher = "centos-container",
                        Sku = "7-5",
                        Version =  "latest",
                    }
                }
            };

            var testsConfigurationFilepath = Path.Combine(OutputTestPath(), PublishContainerImages.TestConfigurationFilename);
            var testsConfigurationJson = JsonSerializeObject(testsConfiguration);

            FileInfo configFile = new FileInfo(testsConfigurationFilepath);
            Directory.CreateDirectory(configFile.DirectoryName);
            File.WriteAllText(testsConfigurationFilepath, testsConfigurationJson);
            PublishContainerImages.WriteLog($"\nWrote configuration file at: {configFile.FullName}, file contents:");
            PublishContainerImages.WriteLog(testsConfigurationJson);
        }

        private static List<ContainerImagePayload> _removeInvalidPayloads(List<ContainerImagePayload> payloads)
        {
            return payloads.Where(payload =>
                payload.TestConfigurationDefinition != null && payload.TestParametersDefinition != null).ToList();
        }

        private static List<ContainerImagePayload> _updateTestConfigAndParametersWithTaggedImage(List<ContainerImagePayload> payloads, List<string> publishedImages)
        {
            foreach (var containerImagePayload in payloads)
            {
                var publishedImageWithTag = publishedImages.Find(publishedImage =>
                {
                    var publishedImageWithoutTag = publishedImage.Split(':').First();
                    return publishedImageWithoutTag == containerImagePayload.ContainerImageDefinition.ContainerImage;
                });

                containerImagePayload.TestParametersDefinition.ContainerImage.Value = publishedImageWithTag;
                containerImagePayload.TestConfigurationDefinition.DockerImage = publishedImageWithTag;
            }

            return payloads;
        }

        private static string OutputTestPath()
        {
            var locationUri = new Uri(Assembly.GetEntryAssembly().GetName().CodeBase);
            var executableDirectory = new FileInfo(locationUri.AbsolutePath).Directory;

            return Path.GetFullPath(Path.Combine(executableDirectory.FullName, RelativePathToTestsDir));
        }

        private static string OutputImagesTxtFilePath()
        {
            var locationUri = new Uri(Assembly.GetEntryAssembly().GetName().CodeBase);
            var executableDirectory = new FileInfo(locationUri.AbsolutePath).Directory;

            return Path.GetFullPath(Path.Combine(executableDirectory.FullName, RelativePathToImageTxtDir));
        }

        private static string JsonSerializeObject(dynamic toSerialize)
        {
            DefaultContractResolver contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };

            var jsonSerializerSettings = new JsonSerializerSettings
            {
                ContractResolver = contractResolver,
                Formatting = Formatting.Indented
            };

            return JsonConvert.SerializeObject(toSerialize, jsonSerializerSettings);
        }
    }
}
