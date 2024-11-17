using FolderSync.Configuration;
using MediaBrowser.Controller.Sync;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Net;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using System.IO;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;

namespace FolderSync
{
    public class SyncProvider : IServerSyncProvider, ISupportsDirectCopy
    {
        private readonly IFileSystem _fileSystem;
        private readonly ILogger _logger;
        private readonly IUserManager _userManager;

        public SyncProvider(IFileSystem fileSystem, ILogger logger, IUserManager userManager)
        {
            _fileSystem = fileSystem;
            _logger = logger;
            _userManager = userManager;
        }

        public bool SupportsRemoteSync
        {
            get { return true; }
        }

        public async Task<SyncedFileInfo> SendFile(SyncJob syncJob, string originalMediaPath, Stream inputStream, bool isMedia, string[] outputPathParts, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var fullPath = GetFullPath(outputPathParts, target);

            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(fullPath));

            _logger.Debug("Folder sync saving stream to {0}", fullPath);

            using (var fileStream = _fileSystem.GetFileStream(fullPath, FileOpenMode.Create, FileAccessMode.Write, FileShareMode.Read, true))
            {
                await inputStream.CopyToAsync(fileStream).ConfigureAwait(false);
                return GetSyncedFileInfo(fullPath);
            }
        }

        public Task<bool> DeleteFile(SyncJob syncJob, string path, SyncTarget target, CancellationToken cancellationToken)
        {
            try
            {
                _fileSystem.DeleteFile(path);
            }
            catch (Exception ex)
            {
                _logger.ErrorException("FolderSync: Error removing {0} from {1}.", ex, path, target.Name);
            }

            var parent = _fileSystem.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
            {
                return Task.FromResult(true);
            }

            parent = _fileSystem.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(parent))
            {
                return Task.FromResult(true);
            }

            try
            {
                _logger.Info("Deleting empty sub-folders from {0}", parent);
                DeleteEmptyFolders(parent);
            }
            catch
            {
            }

            parent = _fileSystem.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(parent))
            {
                return Task.FromResult(true);
            }

            try
            {
                _logger.Info("Deleting empty sub-folders from {0}", parent);
                DeleteEmptyFolders(parent);
            }
            catch
            {
            }

            return Task.FromResult(true);
        }

        public Task<Stream> GetFile(string id, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken)
        {
            return Task.FromResult(_fileSystem.OpenRead(id));
        }

        public Task<QueryResult<FileSystemMetadata>> GetFiles(string id, SyncTarget target, CancellationToken cancellationToken)
        {
            var result = new QueryResult<FileSystemMetadata>();

            var file = _fileSystem.GetFileSystemInfo(id);

            if (file.Exists)
            {
                result.TotalRecordCount = 1;
                result.Items = new[] { file }.ToArray();
            }

            return Task.FromResult(result);
        }

        public Task<QueryResult<FileSystemMetadata>> GetFiles(string[] directoryPathParts, SyncTarget target, CancellationToken cancellationToken)
        {
            var result = new QueryResult<FileSystemMetadata>();

            var fullPath = GetFullPath(directoryPathParts, target);

            FileSystemMetadata[] files;

            try
            {
                files = _fileSystem.GetFiles(fullPath, true)
                   .ToArray();
            }
            catch (DirectoryNotFoundException)
            {
                files = Array.Empty<FileSystemMetadata>();
            }

            result.Items = files;
            result.TotalRecordCount = files.Length;

            return Task.FromResult(result);
        }

        public Task<QueryResult<FileSystemMetadata>> GetFiles(SyncTarget target, CancellationToken cancellationToken)
        {
            var account = GetSyncAccounts()
                .FirstOrDefault(i => string.Equals(i.Id, target.Id, StringComparison.OrdinalIgnoreCase));

            if (account == null)
            {
                throw new ArgumentException("Invalid SyncTarget supplied.");
            }

            var result = new QueryResult<FileSystemMetadata>();

            FileSystemMetadata[] files;

            try
            {
                files = _fileSystem.GetFiles(account.Path, true)
                   .ToArray();
            }
            catch (DirectoryNotFoundException)
            {
                files = Array.Empty<FileSystemMetadata>();
            }

            result.Items = files;
            result.TotalRecordCount = files.Length;

            return Task.FromResult(result);
        }

        public string GetFullPath(IEnumerable<string> paths, SyncTarget target)
        {
            var account = GetSyncAccounts()
                .FirstOrDefault(i => string.Equals(i.Id, target.Id, StringComparison.OrdinalIgnoreCase));

            if (account == null)
            {
                throw new ArgumentException("Invalid SyncTarget supplied.");
            }

            var list = paths.ToList();
            list.Insert(0, account.Path);

            return Path.Combine(list.ToArray());
        }

        public string Name
        {
            get { return Plugin.StaticName; }
        }

        public List<SyncTarget> GetSyncTargets(SyncTargetQuery query)
        {
            var accounts = GetSyncAccounts().ToList();

            if (!query.UserId.Equals(0))
            {
                var userIdString = _userManager.GetGuid(query.UserId).ToString("N");

                accounts = accounts
                    .Where(i => i.EnableAllUsers || i.UserIds.Contains(userIdString, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            var list = accounts
                .Select(GetSyncTarget)
                .ToList();

            if (!string.IsNullOrEmpty(query.TargetId))
            {
                list = list.Where(i => string.Equals(i.Id, query.TargetId, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return list;
        }

        private SyncTarget GetSyncTarget(SyncAccount account)
        {
            return new SyncTarget
            {
                Id = account.Id,
                Name = account.Name
            };
        }

        private IEnumerable<SyncAccount> GetSyncAccounts()
        {
            return Plugin.Instance.Configuration.SyncAccounts.ToList();
        }

        private void DeleteEmptyFolders(string parent)
        {
            var folders = _fileSystem.GetDirectoryPaths(parent).ToArray();

            foreach (var directory in folders)
            {
                DeleteEmptyFolders(directory);

                try
                {
                    if (!_fileSystem.GetFileSystemEntryPaths(directory).Any())
                    {
                        _logger.Info("Deleting directory: {0}", directory);

                        _fileSystem.DeleteDirectory(directory, false);
                    }
                }
                catch (Exception ex)
                {
                    _logger.ErrorException("FolderSync: Error during DeleteEmptyFolders in {0}.", ex, directory);
                }
            }
        }

        public Task<SyncedFileInfo> SendFile(SyncJob syncJob, string originalMediaPath, string inputPath, bool isMedia, string[] outputPathParts, SyncTarget target, IProgress<double> progress, CancellationToken cancellationToken)
        {
            var fullPath = GetFullPath(outputPathParts, target);

            _fileSystem.CreateDirectory(_fileSystem.GetDirectoryName(fullPath));

            _logger.Debug("Folder sync copying file from {0} to {1}", inputPath, fullPath);
            _fileSystem.CopyFile(inputPath, fullPath, true);

            return Task.FromResult(GetSyncedFileInfo(fullPath));
        }

        private SyncedFileInfo GetSyncedFileInfo(string path)
        {
            // Normalize the full path to make sure it's consistent with the results you'd get from directory queries
            var file = _fileSystem.GetFileInfo(path);
            path = file.FullName;

            return new SyncedFileInfo
            {
                Path = path,
                Protocol = MediaProtocol.File,
                Id = path
            };
        }
    }
}
