using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core.IO;
using Amazon.S3;
using Amazon.S3.Model;
using System.IO;
using Amazon;
using Umbraco.Core.Logging;

namespace Obviuse.FileSystemProviders
{
    /// <summary>
    /// A file system provider for Umbraco that uses
    /// the Amazon S3 system
    /// </summary>
    public class AmazonS3Provider : IFileSystem
    {
        private const string _HTTP = "http";
        private const string _HTTPS = "https";

        private readonly AmazonS3Client _as3;
        private readonly string _bucketName;
        private readonly RegionEndpoint _region;
        private readonly string _mediaRoot;
        private readonly string _useHttps;

        private const string Delimiter = "/";


        public AmazonS3Provider(string accessKeyId, string secretAccessKey, string bucketName, string region)
        {
            string mediaRoot = null;
            string useHttps = "1";
            _bucketName = bucketName;
            _region = GetRegionEndpoint(region);
            _as3 = new AmazonS3Client(accessKeyId, secretAccessKey, _region);
            _mediaRoot = !String.IsNullOrEmpty(mediaRoot) ? mediaRoot : String.Empty;
            _useHttps = useHttps;
        }

        public string HttpProtocol
        {
            get { return _useHttps == "1" ? _HTTPS : _HTTP; }
        }

        public string S3Domain
        {
            get { return String.Format("{0}.s3.amazonaws.com", _bucketName); }
        }
        /// <summary>
        /// Get the directories from the bucket
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public IEnumerable<string> GetDirectories(string path)
        {
            var directories = new List<string>();
            try
            {
                var request = new ListObjectsRequest
                {
                    BucketName = _bucketName,
                    Delimiter = Delimiter
                };

                if (!string.IsNullOrEmpty(path) && !path.EndsWith(Delimiter))
                {
                    path += Delimiter;
                    request = new ListObjectsRequest
                    {
                        BucketName = _bucketName,
                        Prefix = path
                    };
                }

                var response = _as3.ListObjects(request);
                directories.AddRange(response.CommonPrefixes.Select(directory => directory.TrimEnd("/".ToCharArray())));
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("Exception fetching directories", ex);
            }
            return directories;
        }

        /// <summary>
        /// Delete a directory from S3
        /// </summary>
        /// <param name="path"></param>
        public void DeleteDirectory(string path)
        {
            DeleteDirectory(path, false);
        }

        /// <summary>
        /// Delete a directory recursively from S3
        /// </summary>
        /// <param name="path"></param>
        /// <param name="recursive"></param>
        public void DeleteDirectory(string path, bool recursive)
        {
            LogHelper.Debug<AmazonS3Provider>("Deleting directory: " + path);
            if (!FolderExists(path))
                return;

            try
            {
                var objectsToDelete = new List<string>();

                var listRequest = new ListObjectsRequest
                {
                    BucketName = _bucketName,
                    Prefix = path
                };

                ListObjectsResponse listResponse;
                do
                {
                    // Get a list of objects
                    listResponse = _as3.ListObjects(listRequest);
                    foreach (S3Object obj in listResponse.S3Objects)
                    {
                        objectsToDelete.Add(obj.Key);
                    }

                    // Set the marker property
                    listRequest.Marker = listResponse.NextMarker;
                } while (listResponse.IsTruncated);

                if (objectsToDelete.Any())
                {
                    var deleteRequest = new DeleteObjectsRequest()
                    {
                        BucketName = _bucketName
                    };

                    foreach (var item in objectsToDelete)
                    {
                        deleteRequest.AddKey(item);
                    }

                    _as3.DeleteObjects(deleteRequest);
                }
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("Directory not found", ex);
            }
        }

        /// <summary>
        /// Check if a directory exists in the bucket
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool DirectoryExists(string path)
        {
            LogHelper.Debug<AmazonS3Provider>("Checking if directory exists: " + path);
            return FolderExists(path);
        }

        /// <summary>
        /// Add a file to S3
        /// </summary>
        /// <param name="path"></param>
        /// <param name="stream"></param>
        public void AddFile(string path, Stream stream)
        {
            AddFile(path, stream, true);
        }

        /// <summary>
        /// Add a file to S3
        /// </summary>
        /// <param name="path"></param>
        /// <param name="stream"></param>
        /// <param name="overrideIfExists"></param>
        public void AddFile(string path, Stream stream, bool overrideIfExists)
        {
            LogHelper.Debug<AmazonS3Provider>("Adding file: " + path);
            if (FileExists(path) && !overrideIfExists)
                throw new InvalidOperationException(string.Format("A file at path '{0}' already exists", path));

            var s3AssetName = GetUrlPathWithMediaRoot(path);

            try
            {
                var memoryCopy = new MemoryStream();
                stream.CopyTo(memoryCopy);
                var putObjectRequest = new PutObjectRequest()
                {
                    BucketName = _bucketName,
                    CannedACL = S3CannedACL.PublicRead,
                    Key = s3AssetName,
                    InputStream = memoryCopy
                };

                var responser = _as3.PutObject(putObjectRequest);
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("Could not upload file", ex);
            }
        }

        /// <summary>
        /// Get all the files from S3
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public IEnumerable<string> GetFiles(string path)
        {
            return GetFiles(path, "*.*");
        }

        /// <summary>
        /// Get all the files from S3 by filter, doesn't currently support filter
        /// </summary>
        /// <param name="path"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public IEnumerable<string> GetFiles(string path, string filter)
        {
            LogHelper.Debug<AmazonS3Provider>("Getting files " + path);
            var s3FolderName = PathToUrlPath(Path.GetDirectoryName(path));
            var files = new List<string>();

            try
            {
                var request = new ListObjectsRequest()
                {
                    BucketName = _bucketName,
                    Prefix = path
                };
                var response = _as3.ListObjects(request);
                files.AddRange(response.S3Objects.Select(o => Path.GetFileName(o.Key)));
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("Could not get files", ex);
            }
            return files;
        }

        /// <summary>
        /// Open a file from S3
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public Stream OpenFile(string path)
        {
            LogHelper.Debug<AmazonS3Provider>("Opening file: " + path);
            var s3AssetName = GetUrlPathWithMediaRoot(path);

            var getObjectRequest =
                new GetObjectRequest()
                {
                    BucketName = _bucketName,
                    Key = s3AssetName
                };

            var outboundStream = new MemoryStream();

            // Issue request and remember to dispose of the response
            using (GetObjectResponse response = _as3.GetObject(getObjectRequest))
            {
                var buffer = new byte[2048];
                var count = response.ResponseStream.Read(buffer, 0, buffer.Length);
                while (count > 0)
                {
                    outboundStream.Write(buffer, 0, count);
                    count = response.ResponseStream.Read(buffer, 0, buffer.Length);
                }
                outboundStream.Position = 0;
            }
            return outboundStream;
        }

        /// <summary>
        /// Delete a file
        /// </summary>
        /// <param name="path"></param>
        public void DeleteFile(string path)
        {
            LogHelper.Debug<AmazonS3Provider>("Deleting file: " + path);
            if (!FileExists(path))
                return;

            var s3AssetName = GetUrlPathWithMediaRoot(path);

            try
            {
                var request = new DeleteObjectRequest()
                {
                    BucketName = _bucketName,
                    Key = s3AssetName
                };
                _as3.DeleteObject(request);
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("File could not be deleted", ex);
            }
        }

        /// <summary>
        /// Does the file exist
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool FileExists(string path)
        {
            LogHelper.Debug<AmazonS3Provider>("File exists: " + path);

            var s3AssetName = GetUrlPathWithMediaRoot(path);
            var result = false;

            try
            {
                var fileInfo = new Amazon.S3.IO.S3FileInfo(_as3, _bucketName, s3AssetName);
                result = fileInfo.Exists;
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("File existence could not be confirmed.", ex);
            }

            return result;
        }

        /// <summary>
        /// Get the relative path of the media item
        /// </summary>
        /// <param name="fullPathOrUrl"></param>
        /// <returns></returns>
        public string GetRelativePath(string fullPathOrUrl)
        {
            LogHelper.Debug<AmazonS3Provider>("Relative path: " + fullPathOrUrl);
            if (String.IsNullOrEmpty(fullPathOrUrl))
                return fullPathOrUrl;

            return GetFullPath(RemoveProtocolAndDomain(fullPathOrUrl));
        }

        /// <summary>
        /// Get the full path of the media item
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public string GetFullPath(string path)
        {
            return GetUrlPathWithMediaRoot(path);
        }

        /// <summary>
        /// Get the full URL of AWS
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public string GetUrl(string path)
        {
            LogHelper.Debug<AmazonS3Provider>("Get URL: " + path);

            if (String.IsNullOrEmpty(path))
                return String.Empty;

            return path.StartsWith(_HTTP)
                ? path
                : BuildPersistedUrl(path);
        }

        /// <summary>
        /// Get the last modified date
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public DateTimeOffset GetLastModified(string path)
        {
            LogHelper.Debug<AmazonS3Provider>("Get Last Modified Date: " + path);
            var s3AssetName = GetUrlPathWithMediaRoot(path);

            try
            {
                GetObjectMetadataRequest request =
                    new GetObjectMetadataRequest()
                    {
                        BucketName = _bucketName,
                        Key = s3AssetName
                    };
                var response = _as3.GetObjectMetadata(request);
                return response.LastModified;
            }
            catch (AmazonS3Exception ex)
            {
                LogHelper.Error<AmazonS3Provider>("Could not get last modified date", ex);
            }
            return DateTimeOffset.MinValue;
        }

        /// <summary>
        /// Get the created date of the file, this isn't supported in Amazon S3 so we're returning last modified date
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public DateTimeOffset GetCreated(string path)
        {
            return GetLastModified(path);
        }

        #region Helper Methods

        /// <summary>
        /// Check if the Amazon bucket exists
        /// </summary>
        /// <param name="bucketName"></param>
        /// <returns></returns>
        protected bool BucketExists(string bucketName)
        {
            var response = _as3.ListBuckets(); // Get the bucket first

            return response.Buckets.Any(bucket => bucket.BucketName == bucketName);
        }

        /// <summary>
        /// Check if the folder exists
        /// </summary>
        /// <param name="folder"></param>
        /// <returns></returns>
        protected bool FolderExists(string folder)
        {
            if (!folder.EndsWith(Delimiter))
            {
                folder += Delimiter;
            }
            var request =
                new ListObjectsRequest()
                {
                    BucketName = _bucketName,
                    Delimiter = Delimiter,
                    Prefix = folder

                };
            var response = _as3.ListObjects(request);
            return response.S3Objects.Any();
        }

        /// <summary>
        /// Get the region endpoint from the specified text, this should match
        /// the start of the URL as per the S3 urls here:
        /// http://docs.aws.amazon.com/general/latest/gr/rande.html#s3_region
        /// e.g. eu-west-1
        /// 
        /// </summary>
        /// <param name="region"></param>
        /// <returns></returns>
        protected RegionEndpoint GetRegionEndpoint(string region)
        {
            var regions = new Dictionary<string, RegionEndpoint>
            {
                {"us-west-1", RegionEndpoint.USWest1},
                {"us-west-2", RegionEndpoint.USWest2},
                {"us-east-1", RegionEndpoint.USEast1},
                {"eu-west-1", RegionEndpoint.EUWest1},
                {"eu-central-1", RegionEndpoint.EUCentral1},
                {"ap-southeast-1", RegionEndpoint.APSoutheast1},
                {"ap-southeast-2", RegionEndpoint.APSoutheast2},
                {"ap-northeast-1", RegionEndpoint.APNortheast1},
                {"sa-east-1", RegionEndpoint.SAEast1}
            };
            return regions[region];
        }

        protected string GetUrlPathWithMediaRoot(string path)
        {
            if (path.StartsWith(_HTTP))
                return path;

            if (String.IsNullOrEmpty(_mediaRoot))
                return PathToUrlPath(path);

            if (path.Equals(Delimiter))
                return _mediaRoot + Delimiter;

            return path.StartsWith(_mediaRoot)
                ? PathToUrlPath(path)
                : PathToUrlPath(Path.Combine(_mediaRoot, path));
        }

        protected string PathToUrlPath(string path)
        {
            return String.IsNullOrEmpty(path)
                ? String.Empty
                : path.Equals(Delimiter)
                    ? Delimiter
                    : path.Replace(Path.DirectorySeparatorChar, '/');
        }

        protected string BuildPersistedUrl(string path)
        {
            return String.Format("{0}/{1}", GetProtocolAndDomain(),
                GetUrlPathWithMediaRoot(PathToUrlPath(path)));
        }

        protected string GetProtocolAndDomain()
        {
            return String.Format("{0}://{1}",
                HttpProtocol, S3Domain);
        }

        protected string RemoveProtocolAndDomain(string path)
        {
            return path.Replace(String.Format("{0}/", GetProtocolAndDomain()), String.Empty);
        }

        #endregion
    }
}