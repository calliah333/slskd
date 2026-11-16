// <copyright file="ShareScanner.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

using System.IO;

namespace slskd.Shares
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Serilog;
    using slskd.Files;

    /// <summary>
    ///     Shared file cache.
    /// </summary>
    public interface IShareScanner
    {
        /// <summary>
        ///     Gets the cache state monitor.
        /// </summary>
        IStateMonitor<SharedFileCacheState> StateMonitor { get; }

        /// <summary>
        ///     Scans the configured shares and fills the cache.
        /// </summary>
        /// <param name="shares">The list of shares from which to fill the cache.</param>
        /// <param name="options">The current options snapshot.</param>
        /// <param name="repository">The repository to update.</param>
        /// <returns>The operation context.</returns>
        Task ScanAsync(IEnumerable<Share> shares, Options.SharesOptions options, IShareRepository repository);

        /// <summary>
        ///     Cancels the currently running fill operation, if one is running.
        /// </summary>
        /// <returns>A value indicating whether a fill operation was cancelled.</returns>
        bool TryCancelScan();
    }

    /// <summary>
    ///     Shared file cache.
    /// </summary>
    public class ShareScanner : IShareScanner
    {
        /// <summary>
        ///     The approximate number of rows (directories and files) committed per transaction during a scan.
        /// </summary>
        /// <remarks>
        ///     Larger batches amortize commit overhead further, but hold the write lock (which blocks searches against shared
        ///     cache databases) for longer.
        /// </remarks>
        private const int WriteBatchSize = 1000;

        /// <summary>
        ///     The maximum number of scanned directories waiting to be written.
        /// </summary>
        private const int WriteChannelCapacity = 256;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ShareScanner"/> class.
        /// </summary>
        /// <param name="workerCount"></param>
        /// <param name="fileService"></param>
        /// <param name="soulseekFileFactory"></param>
        public ShareScanner(
            int workerCount,
            FileService fileService,
            ISoulseekFileFactory soulseekFileFactory = null)
        {
            WorkerCount = workerCount;
            Files = fileService;
            SoulseekFileFactory = soulseekFileFactory ?? new SoulseekFileFactory(fileService: Files);

            Flags = Program.Flags;
        }

        /// <summary>
        ///     Gets the cache state monitor.
        /// </summary>
        public IStateMonitor<SharedFileCacheState> StateMonitor => State;

        private FileService Files { get; }
        private int WorkerCount { get; }
        private ILogger Log { get; } = Serilog.Log.ForContext<ShareScanner>();
        private IReadOnlyList<Share> Shares { get; set; }
        private ISoulseekFileFactory SoulseekFileFactory { get; }
        private IManagedState<SharedFileCacheState> State { get; } = new ManagedState<SharedFileCacheState>();
        private SemaphoreSlim SyncRoot { get; } = new SemaphoreSlim(1);
        private CancellationTokenSource CancellationTokenSource { get; set; }
        private Options.FlagsOptions Flags { get; set; }

        /// <summary>
        ///     Scans the configured shares and fills the cache.
        /// </summary>
        /// <param name="shares">The list of shares from which to fill the cache.</param>
        /// <param name="options">The current options snapshot.</param>
        /// <param name="repository">The repository to update.</param>
        /// <returns>The operation context.</returns>
        public async Task ScanAsync(IEnumerable<Share> shares, Options.SharesOptions options, IShareRepository repository)
        {
            // obtain the semaphore, or fail if it can't be obtained immediately, indicating that a scan is running.
            if (!await SyncRoot.WaitAsync(millisecondsTimeout: 0))
            {
                Log.Warning("Shared file scan rejected; scan is already in progress.");
                throw new ShareScanInProgressException("Shared files are already being scanned.");
            }

            CancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(Program.MasterCancellationTokenSource.Token);
            var cancellationToken = CancellationTokenSource.Token;

            // send control back to the calling context. if we don't do this, the caller will block until
            // directories have been enumerated.
            await Task.Yield();

            try
            {
                State.SetValue(state => state with
                {
                    Filling = true,
                    Filled = false,
                    Cancelled = false,
                    FillProgress = 0,
                    Directories = 0,
                    Files = 0,
                    ExcludedDirectories = 0,
                });

                // it's possible that the database was tampered with between the time it was checked at startup and now
                // validate the tables, and if there's an issue, drop and recreate everything.
                if (!repository.TryValidate())
                {
                    Log.Warning("Shared file cache missing or invalid. Re-creating prior to scan.");
                    repository.Create(discardExisting: true);
                    Log.Information("Shared file cache re-created and ready for scan.");
                }

                var regexOptions = RegexOptions.Compiled;

                if (!Flags.CaseSensitiveRegEx)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                var filters = options.Filters
                    .Select(filter => new Regex(filter, regexOptions))
                    .ToList();

                Log.Information("Starting shared file scan");

                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                repository.InsertScan(timestamp, options);

                var sw = new Stopwatch();
                var swSnapshot = 0L;
                sw.Start();

                // note: this should be the only place where we are assigning Shares, so that this is 1) synchronized by SyncRoot
                // and 2) stable throughout the scan.  if this ever changes we'll have to snapshot the value and use it instead
                Shares = shares.ToList();

                if (!Shares.Any())
                {
                    Log.Warning("Aborting shared file scan; no shares configured.");
                }

                foreach (var share in Shares)
                {
                    Log.Debug(share.ToJson());
                }

                Shares.Where(s => !s.IsExcluded).ToList()
                    .ForEach(s => Log.Information("Sharing {Local} as {Remote}", s.LocalPath, s.RemotePath));

                Shares.Where(s => s.IsExcluded).ToList()
                    .ForEach(s => Log.Information("Excluding {Local}", s.LocalPath));

                Log.Information("Enumerating shared directories");
                swSnapshot = sw.ElapsedMilliseconds;

                // note: there's a safer version of this in the FileSafety class that checks for traversal and navigates
                // case sensitivity for different operating systems. this version is stripped down for speed, and because
                // we're not dealing with untrusted data
                static bool IsSubDirectoryOf(string subDirectory, string root, StringComparison stringComparison = StringComparison.OrdinalIgnoreCase)
                {
                    return subDirectory.TrimEnd('/', '\\').Equals(root.TrimEnd('/', '\\'), stringComparison) || // subdirectory literally is the root
                        subDirectory.StartsWith(root.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, stringComparison);
                }

                /*
                    derive a list of all subdirectories to scan by enumerating the subdirectories within each configured
                    share recursively, omitting any directory that matches a filter or that exists within an excluded share

                    we don't enumerate excluded shares because 1) the exclusion is a subdirectory of another share and it
                    will be enumerated once already or 2) the exclusion is not a subdirectory of another share and we don't
                    want to enumerate its directories just to remove them all. this is probably obvious and kind of a worthless comment
                */
                var unmaskedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var excludedPaths = Shares.Where(s => s.IsExcluded).Select(s => s.LocalPath).ToHashSet();

                foreach (var share in Shares)
                {
                    if (share.IsExcluded)
                    {
                        continue;
                    }

                    try
                    {
                        var directories = Directory.EnumerateDirectories(share.LocalPath, "*", new EnumerationOptions()
                        {
                            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                            IgnoreInaccessible = true,
                            RecurseSubdirectories = true,
                        });

                        unmaskedDirectories.Add(share.LocalPath);

                        foreach (var directory in directories)
                        {
                            if (excludedPaths.Any(excludedPath => IsSubDirectoryOf(subDirectory: directory, root: excludedPath)))
                            {
                                excludedDirectories.Add(directory);
                                Log.Debug("Excluding directory {Directory} because it is a subdirectory of an excluded share", directory);
                                continue;
                            }

                            if (filters.Any(f => f.IsMatch(directory)))
                            {
                                excludedDirectories.Add(directory);
                                Log.Debug("Excluding directory {Directory} because it matches one or more share filters", directory);
                                continue;
                            }

                            unmaskedDirectories.Add(directory);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to scan share {Directory}: {Message}", share.LocalPath, ex.Message);
                    }
                }

                State.SetValue(state => state with { Directories = unmaskedDirectories.Count, ExcludedDirectories = excludedDirectories.Count });
                Log.Information("Found {Directories} shared directories (and {Excluded} were excluded) in {Elapsed}ms.  Starting file scan.", unmaskedDirectories.Count, excludedDirectories.Count, sw.ElapsedMilliseconds - swSnapshot);
                swSnapshot = sw.ElapsedMilliseconds;

                var current = 0;
                var cached = 0;
                var filtered = 0;

                // set up a function to reliably resolve the Share given a directory; users can define nested shares,
                // so we need to sort to ensure the deepest-nested share is selected first
                var sortedShares = Shares
                    .Where(s => !s.IsExcluded)
                    .OrderByDescending(s => s.LocalPath.Length)
                    .ToList();

                Share ResolveShareFromDirectory(string directory)
                {
                    return sortedShares.First(share => IsSubDirectoryOf(subDirectory: directory, root: share.LocalPath));
                }

                // set up a channel to fan out for directory scanning
                var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1000)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = false,
                    SingleWriter = true,
                });

                /*
                    workers read file metadata in parallel, but SQLite allows only one writer at a time, and committing each
                    row in its own transaction is very slow. workers hand each scanned directory to a single writer, which
                    commits them in batches. the channel is bounded to limit memory if the database falls behind.
                */
                var writes = Channel.CreateBounded<ScannedDirectory>(new BoundedChannelOptions(WriteChannelCapacity)
                {
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = false,
                });

                var writer = Task.Run(() => WriteAsync(writes, repository, timestamp, CancellationTokenSource));

                // create workers to perform the fan out
                var workers = new List<ChannelReader<string>>();

                foreach (var id in Enumerable.Range(0, WorkerCount))
                {
                    workers.Add(new ChannelReader<string>(
                        channel: channel,
                        cancellationToken: cancellationToken,
                        handler: (directory) =>
                        {
                            Log.Debug("Starting scan of {Directory} ({Current}/{Total})", directory, current + 1, unmaskedDirectories.Count);

                            var addedFiles = 0;
                            var filteredFiles = 0;

                            var share = ResolveShareFromDirectory(directory);

                            var maskedDirectory = directory.ReplaceFirst(share.LocalPath, share.RemotePath).NormalizePathForSoulseek();
                            var scannedFiles = new List<ScannedFile>();

                            // recursively find all files in the directory and stick a record in a dictionary, keyed on the sanitized
                            // filename and with a value of a Soulseek.File object
                            try
                            {
                                // enumerate files in this directory only (no subdirectories) exclude hidden and system files and anything
                                // that can't be accessed due to security restrictions
                                var newFileInfos = new DirectoryInfo(directory).EnumerateFiles("*", new EnumerationOptions()
                                {
                                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
                                    IgnoreInaccessible = true,
                                    RecurseSubdirectories = false,
                                });

                                // merge the new dictionary with the rest this will overwrite any duplicate keys, but keys are the fully
                                // qualified name the only time this *should* cause problems is if one of the shares is a subdirectory of another.
                                foreach (var info in newFileInfos)
                                {
                                    if (filters.Any(filter => filter.IsMatch(info.FullName)))
                                    {
                                        filteredFiles++;
                                        continue;
                                    }

                                    var file = SoulseekFileFactory.Create(
                                        filename: info.FullName,
                                        maskedFilename: info.FullName.ReplaceFirst(share.LocalPath, share.RemotePath).NormalizePathForSoulseek());

                                    scannedFiles.Add(new ScannedFile(OriginalFilename: info.FullName, TouchedAt: info.LastWriteTimeUtc, File: file));
                                    addedFiles++;
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug("Failed to scan files in directory {Directory}: {Exception}", directory, ex);
                                Log.Warning("Failed to scan files in directory {Directory}: {Message}", directory, ex.Message);
                            }

                            // the directory and any files found before a failure are written either way, as when each was
                            // inserted individually. if the writer has failed, this throws ChannelClosedException and the worker stops
                            var scanned = new ScannedDirectory(maskedDirectory, scannedFiles);

                            if (!writes.Writer.TryWrite(scanned))
                            {
                                writes.Writer.WriteAsync(scanned).AsTask().GetAwaiter().GetResult();
                            }

                            Interlocked.Increment(ref current);
                            Interlocked.Add(ref filtered, filteredFiles);
                            Interlocked.Add(ref cached, addedFiles);

                            Log.Debug("Finished scanning {Directory}: {Added} files added and {Filtered} filtered", directory, addedFiles, filteredFiles);
                            State.SetValue(state => state with { FillProgress = current / (double)unmaskedDirectories.Count, Files = cached });
                        }));
                }

                Log.Debug("Starting workers...");
                workers.ForEach(w => w.Start());
                Log.Debug("All workers started");

                try
                {
                    try
                    {
                        Log.Debug("Filling DirectoryChannel...");
                        foreach (var directory in unmaskedDirectories)
                        {
                            await channel.Writer.WriteAsync(directory, cancellationToken);
                        }

                        Log.Debug("DirectoryChannel filled");
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Warning("Shared file scan cancellation requested");
                        Log.Debug("DirectoryChannel fill aborted");
                    }
                    catch (ChannelClosedException)
                    {
                        // a worker failed and completed the channel with its exception; awaiting the workers below
                        // rethrows that exception, which faults the scan
                        Log.Debug("DirectoryChannel fill aborted; a worker failed");
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }

                    Log.Debug("Waiting for workers to finish working...");

                    try
                    {
                        await Task.WhenAll(workers.Select(w => w.Completed));
                    }
                    finally
                    {
                        // whether the workers finished or were cancelled, wait for everything they produced to be committed.
                        // if the writer failed, this throws its exception, which faults the scan
                        writes.Writer.TryComplete();
                        await writer;
                    }

                    Log.Debug("All workers finished");

                    Log.Information("Scan found {Files} files, cached {Cached}, and filtered {Filtered} in {Elapsed}ms", cached + filtered, cached, filtered, sw.ElapsedMilliseconds - swSnapshot);
                    swSnapshot = sw.ElapsedMilliseconds;

                    var deletedFiles = repository.PruneFiles(olderThanTimestamp: timestamp);
                    var deletedDirectories = repository.PruneDirectories(olderThanTimestamp: timestamp);

                    Log.Information("Removed or renamed {Files} files and {Directories} directories in {Elapsed}ms", deletedFiles, deletedDirectories, sw.ElapsedMilliseconds - swSnapshot);

                    Log.Information("Rebuilding filename index...");
                    repository.RebuildFilenameIndex();
                    Log.Information("Filename index rebuild complete");

                    Log.Debug("Vacuuming...");
                    repository.Vacuum();
                    Log.Debug("Vacuumed successfully");

                    Log.Debug("Checkpointing...");
                    repository.Checkpoint();
                    Log.Debug("Checkpointed successfully");
                }
                catch (OperationCanceledException)
                {
                    // important! don't try to delete stale files in this case; unscanned records will have a stale timestamp
                    Log.Warning("Shared file scan cancelled successfully");

                    State.SetValue(state => state with { Cancelled = true });
                }

                repository.UpdateScan(timestamp, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                var directoryCount = repository.CountDirectories();
                var fileCount = repository.CountFiles();

                State.SetValue(state => state with
                {
                    Filling = false,
                    Faulted = false,
                    Filled = true,
                    FillProgress = 1,
                    Directories = directoryCount,
                    Files = fileCount,
                });

                Log.Debug($"Shared file cache created or updated in {sw.ElapsedMilliseconds}ms.  Directories: {directoryCount}, Files: {fileCount}");
            }
            catch (Exception ex)
            {
                State.SetValue(state => state with { Filling = false, Faulted = true, Filled = false, FillProgress = 0 });
                Log.Warning(ex, "Encountered error during scan of shared files: {Message}", ex.Message);
                throw;
            }
            finally
            {
                var cts = CancellationTokenSource;
                CancellationTokenSource = null;
                cts?.Dispose();

                SyncRoot.Release();
            }
        }

        /// <summary>
        ///     Writes scanned directories from the specified <paramref name="channel"/> to the <paramref name="repository"/>
        ///     in batches until the channel is completed.
        /// </summary>
        /// <remarks>
        ///     If a write fails, the channel is completed with the exception so that workers blocked writing to it are
        ///     released, and the scan is cancelled so that the directory producer stops.
        /// </remarks>
        /// <param name="channel">The channel from which to read scanned directories.</param>
        /// <param name="repository">The repository to update.</param>
        /// <param name="timestamp">The timestamp of the scan.</param>
        /// <param name="cancellationTokenSource">The source used to cancel the scan if a write fails.</param>
        /// <returns>The operation context.</returns>
        private static async Task WriteAsync(Channel<ScannedDirectory> channel, IShareRepository repository, long timestamp, CancellationTokenSource cancellationTokenSource)
        {
            var batch = new List<ScannedDirectory>();
            var rows = 0;

            try
            {
                // intentionally not cancellable; after a cancellation everything the workers already produced is committed
                await foreach (var directory in channel.Reader.ReadAllAsync())
                {
                    batch.Add(directory);
                    rows += 1 + directory.Files.Count;

                    if (rows >= WriteBatchSize)
                    {
                        repository.InsertBatch(batch, timestamp);
                        batch.Clear();
                        rows = 0;
                    }
                }

                if (batch.Count > 0)
                {
                    repository.InsertBatch(batch, timestamp);
                }
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
                cancellationTokenSource.Cancel(swallowObjectDisposedException: true);
                throw;
            }
        }

        /// <summary>
        ///     Cancels the currently running fill operation, if one is running.
        /// </summary>
        /// <returns>A value indicating whether a fill operation was cancelled.</returns>
        public bool TryCancelScan()
        {
            if (CancellationTokenSource != null)
            {
                CancellationTokenSource.Cancel(swallowObjectDisposedException: true);
                return true;
            }

            return false;
        }
    }
}