﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using PublishContainerImages.Model;


namespace PublishContainerImages
{
    class PublishContainerImages
    {

        public static Action<string> WriteLog;
        public static Action<string> WriteError;

        public static string ContainerImageDefinitionFilename = "containerImage.json";
        public static string TestConfigurationFilename = "testConfiguration.json";
        public static string TestParametersFilename = "testParameters.json";

        private const string PublishedImagesFilename = "publishedImages.txt";
        private const string LogFilename = "publishContainerImages.log";

        private const string StorageAccountName = "renderingapplications";
        private const string StoragContainerName = "batch-rendering-apps";

        private const string OutputTestPath = "../Tests";

        private static StreamWriter _log;

        static void Main(string[] args)
        {
            using (var log = File.AppendText(LogFilename))
            {
              try
              {
                    _log = log;
                    _log.AutoFlush = true;
                    WriteLog = _writeLog;
                    WriteError = _writeError;
                    _writeLog($"Beginning New Publishing Run with args: {string.Join(", ", args)}");
                    
                    var storageKey = args[0];
                    var targetFolder = new DirectoryInfo(args[1]);
                    var traversalMode = (TraversalMode)Enum.Parse(typeof(TraversalMode), args[2], true);
                    var gitCommitSha = args[3];
                    var buildImages = bool.Parse(args[4]);
                    
                    //var overwrite = bool.Parse(args[6]); TODO if false, only build and publish images which don't already exist for a given version tag, might need to check these on repo rather than local, if found local could maybe just redo the push?

                    var blobContainer = _buildBlobClient(buildImages, StorageAccountName, storageKey, StoragContainerName); //NOTE blobContainer will be null if !buildImages

                    var containerImagePayload = DirectoryTraversal.BuildPayloadFromDirectoryTree(targetFolder, traversalMode, new List<ContainerImagePayload>());

                    _writePrePublishLog(containerImagePayload);
                    var imageNumber = 1;
                    var publishedImages = new List<string>();

                    foreach (var imageDef in containerImagePayload.Select(x => x.ContainerImageDefinition))
                    {
                        _writeLog($"Publishing #{imageNumber++} of {containerImagePayload.Count} - {imageDef.ContainerImage}");

                        if (buildImages)
                        {
                            dynamic blobProperties =
                                _getBlobUriWithSasTokenAndMD5(imageDef.InstallerFileBlob, blobContainer);

                            var localImageId = _buildImage(imageDef, blobProperties.blobSasToken);

                            var tag = ImageTagging._fetchImageTag(blobProperties.blobMD5, gitCommitSha);

                            DockerCommands._runDockerTag(imageDef, localImageId, tag);

                            var builtImage = $"{imageDef.ContainerImage}:{tag}";
                            
                            _writeLog($"Successfully built and tagged {builtImage}");

                            DockerCommands._runDockerPush(imageDef, tag);
                            _writeLog($"Successfully published {builtImage}\n");

                            publishedImages.Add(builtImage);
                        }
                    }

                    containerImagePayload = _removeInvalidPayloads(containerImagePayload);
                    containerImagePayload = _updateTestConfigAndParametersWithTaggedImage(containerImagePayload, publishedImages);
                    _outputTestFiles(containerImagePayload);
                    _outputBuiltImages(publishedImages);
                    _writeLog($"Completed Publishing Successfully!\n\n");
                }
            
              catch (Exception ex)
              {
                  _writeError("Fatal Exception: " + ex);
                  throw ex;
              }
            }
        }

        private static List<ContainerImagePayload> _removeInvalidPayloads(List<ContainerImagePayload> payloads)
        {
            return payloads.Where(payload =>
                payload.TestConfigurationDefinition != null && payload.TestParametersDefinition != null).ToList();
        }

        private static void _outputTestFiles(List<ContainerImagePayload> payloads)
        {
            var testsConfiguration = new TestsDefinition
            {
                Tests = payloads.Select(payload =>
                {
                    var config = payload.TestConfigurationDefinition;
                    config.Parameters = Path.Combine("../", OutputTestPath, config.Parameters).Replace("\\", "/");
                    return config;
                }).ToArray(),
                Images = new []
                {
                    new MarketplaceImageDefinition
                    {
                        Offer = "microsoft-azure-batch",
                        OsType = "linux",
                        Publisher = "centos-container",
                        Sku = "7-5",
                        Version =  "latest",
                    }
                }
            };

            payloads.ForEach(payload =>
            {
                var parametersPath = Path.Combine(OutputTestPath, payload.TestConfigurationDefinition.Parameters);
                var parametersJson = JsonConvert.SerializeObject(payload.TestParametersDefinition);
                FileInfo paramsFile = new FileInfo(parametersPath);
                Directory.CreateDirectory(paramsFile.DirectoryName);
                File.WriteAllText(parametersPath, parametersJson);
            });

            var testsConfigurationFilepath = Path.Combine(OutputTestPath, TestConfigurationFilename);
            var testsConfigurationJson = JsonConvert.SerializeObject(testsConfiguration);

            FileInfo configFile = new FileInfo(testsConfigurationFilepath);
            Directory.CreateDirectory(configFile.DirectoryName);
            File.WriteAllText(testsConfigurationFilepath, testsConfigurationJson);
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

        private static void _writePrePublishLog(List<ContainerImagePayload> containerImages)
        {
            var imageListLog = ($"Loaded {containerImages.Count} containerImages:\n");
            containerImages.ForEach(image => imageListLog += image.ContainerImageDefinition.ContainerImage + "\n");
            _writeLog(imageListLog);
        }
   
        private static void _writeLog(string log)
        {
            var logLine = string.Format(@"{0}: {1}", DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToLongTimeString(), log);
            _log.WriteLine(logLine);
            Console.WriteLine(log);
        }

        private static void _writeError(string error)
        {
            var logLine = string.Format(@"{0}: ERROR: {1}", DateTime.UtcNow.ToShortDateString() + " " + DateTime.UtcNow.ToLongTimeString(), error);
            _log.WriteLine(logLine);
            Console.WriteLine($"ERROR: {error}");
        }

        private static void _outputBuiltImages(List<string> publishedImages)
        {
            _writeLog("Published the following images:");
            publishedImages.ForEach(_writeLog);
            File.WriteAllLines(PublishedImagesFilename, publishedImages);
        }

        private static string _buildImage(ContainerImageDefinition imageDefinition, string blobSasToken)
        {
            var dockerBuildOutput = DockerCommands._runDockerBuild(blobSasToken, imageDefinition);

            var localImageId = _imageIdFromDockerBuildOutput(dockerBuildOutput);

            return localImageId;
        }

        private static string _imageIdFromDockerBuildOutput(string[] output)
        {
            var keyLine = output.First(line => line.StartsWith("Successfully built "));

            var imageId = keyLine.Substring("Successfully built ".Length);

            return imageId;
        }

        private static CloudBlobContainer _buildBlobClient(bool buildImages, string storageAccountName, string storageKey, string containerName)
        {
            if (!buildImages)
            {
                return null;
            }

            var storageUri = new Uri($"https://{storageAccountName}.blob.core.windows.net/");

            var storageClient = new CloudBlobClient(storageUri, new StorageCredentials(storageAccountName, storageKey));

            return storageClient.GetContainerReference(containerName);
        }

        private static dynamic _getBlobUriWithSasTokenAndMD5(string blobPath, CloudBlobContainer blobContainer)
        {
            if (string.IsNullOrEmpty(blobPath))
            {
                return new { blobSasToken = string.Empty, blobMD5 = string.Empty };
            }

            var blob = blobContainer.GetBlockBlobReference(blobPath);

            var sasConstraints =
                new SharedAccessBlobPolicy
                {
                    SharedAccessStartTime = DateTimeOffset.UtcNow.AddMinutes(-5),
                    SharedAccessExpiryTime = DateTimeOffset.UtcNow.AddHours(24),
                    Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.Write
                };

            var sasBlobToken = blob.GetSharedAccessSignature(sasConstraints);

            blob.FetchAttributesAsync().GetAwaiter().GetResult();

            return new { blobSasToken = blob.Uri + sasBlobToken, blobMD5 = blob.Properties.ContentMD5 };
        }
    }
}
