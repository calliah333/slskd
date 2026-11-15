// <copyright file="UsersController.cs" company="JP Dillingham">
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

using Microsoft.Extensions.Options;

namespace slskd.Users.API
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Asp.Versioning;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Mvc;
    using Serilog;
    using Soulseek;

    /// <summary>
    ///     Users.
    /// </summary>
    [Route("api/v{version:apiVersion}/[controller]")]
    [ApiVersion("0")]
    [ApiController]
    [Produces("application/json")]
    [Consumes("application/json")]
    public class UsersController : ControllerBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="UsersController"/> class.
        /// </summary>
        /// <param name="soulseekClient"></param>
        /// <param name="browseTracker"></param>
        /// <param name="browseCache"></param>
        /// <param name="userService"></param>
        /// <param name="optionsSnapshot"></param>
        public UsersController(ISoulseekClient soulseekClient, IBrowseTracker browseTracker, IBrowseCache browseCache, IUserService userService, IOptionsSnapshot<Options> optionsSnapshot)
        {
            Client = soulseekClient;
            BrowseTracker = browseTracker;
            BrowseCache = browseCache;
            Users = userService;
            OptionsSnapshot = optionsSnapshot;
        }

        private IBrowseTracker BrowseTracker { get; }
        private IBrowseCache BrowseCache { get; }
        private ISoulseekClient Client { get; }
        private IUserService Users { get; }
        private IOptionsSnapshot<Options> OptionsSnapshot { get; }
        private ILogger Log { get; set; } = Serilog.Log.ForContext<UsersController>();

        /// <summary>
        ///     Retrieves the address of the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The username of the user.</param>
        /// <returns></returns>
        /// <response code="200">The request completed successfully.</response>
        [HttpGet("{username}/endpoint")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(IPEndPoint), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Endpoint([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            try
            {
                var endpoint = await Users.GetIPEndPointAsync(username);
                return Ok(endpoint);
            }
            catch (UserOfflineException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        ///     Retrieves the files shared by the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The username of the user.</param>
        /// <returns></returns>
        [HttpGet("{username}/browse")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(IEnumerable<Directory>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Browse([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            try
            {
                var result = await Client.BrowseAsync(username);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    BrowseTracker.TryRemove(username);
                });

                BrowseCache.AddOrUpdate(username, result);

                return Ok(result);
            }
            catch (UserOfflineException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        ///     Browses the specified <paramref name="username"/>, caches the result, and retrieves the shared directories without files.
        /// </summary>
        /// <remarks>
        ///     Files are retrieved from the cached result with <see cref="BrowseFiles"/>.
        /// </remarks>
        /// <param name="username">The username of the user.</param>
        /// <returns></returns>
        [HttpGet("{username}/browse/directories")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(BrowseDirectoriesResponse), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> BrowseDirectories([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            try
            {
                var result = await Client.BrowseAsync(username);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    BrowseTracker.TryRemove(username);
                });

                var entry = BrowseCache.AddOrUpdate(username, result);

                return Ok(new BrowseDirectoriesResponse
                {
                    DirectoryCount = entry.DirectoryCount,
                    FileCount = entry.FileCount,
                    LockedDirectoryCount = entry.LockedDirectoryCount,
                    LockedFileCount = entry.LockedFileCount,
                    Directories = entry.Directories
                        .Select(d => new BrowseDirectoryResponse { Name = d.Directory.Name, FileCount = d.Directory.FileCount, Locked = d.Locked })
                        .ToList(),
                });
            }
            catch (UserOfflineException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        ///     Retrieves the files in the specified directory, and optionally its descendants, from the cached result of the most
        ///     recent browse of the specified <paramref name="username"/>.
        /// </summary>
        /// <remarks>
        ///     Never contacts the user. Files are included in order until the limit is reached; directories after that point are omitted.
        /// </remarks>
        /// <param name="username">The username of the user.</param>
        /// <param name="request">The files request.</param>
        /// <returns></returns>
        /// <response code="400">The directory is missing.</response>
        /// <response code="404">The user has no cached browse result, or the directory is not in it; browse again.</response>
        [HttpPost("{username}/browse/files")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(BrowseFilesResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public IActionResult BrowseFiles([FromRoute, UrlEncoded, Required] string username, [FromBody, Required] BrowseFilesRequest request)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (request == null || string.IsNullOrEmpty(request.Directory))
            {
                return BadRequest();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            if (!BrowseCache.TryGet(username, out var entry))
            {
                return NotFound($"No cached browse results for {username}; browse again");
            }

            // when recursive, a directory that is only implied by its descendants (not itself in the response) is still found
            var matches = entry.Find(request.Directory, request.Recursive);

            if (matches.Count == 0)
            {
                return NotFound("Directory not found");
            }

            var limit = Math.Clamp(request.Limit, 1, BrowseFilesRequest.MaxLimit);
            var total = matches.Sum(d => d.Directory.FileCount);
            var included = 0;
            var directories = new List<BrowseDirectoryFilesResponse>(matches.Count);

            foreach (var (directory, locked) in matches)
            {
                if (included == limit && included < total)
                {
                    break;
                }

                var take = Math.Min(directory.FileCount, limit - included);

                directories.Add(new BrowseDirectoryFilesResponse
                {
                    Name = directory.Name,
                    FileCount = directory.FileCount,
                    Locked = locked,
                    Files = take == directory.FileCount ? directory.Files : directory.Files.Take(take).ToList(),
                });

                included += take;
            }

            return Ok(new BrowseFilesResponse
            {
                Total = total,
                Truncated = included < total,
                Directories = directories,
            });
        }

        /// <summary>
        ///     Retrieves the status of the current browse operation for the specified <paramref name="username"/>, if any.
        /// </summary>
        /// <param name="username">The username of the user.</param>
        /// <returns></returns>
        [HttpGet("{username}/browse/status")]
        [Authorize(Policy = AuthPolicy.Any)]
        [ProducesResponseType(typeof(decimal), 200)]
        [ProducesResponseType(404)]
        public IActionResult BrowseStatus([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (BrowseTracker.TryGet(username, out var progress))
            {
                return Ok(progress);
            }

            return NotFound();
        }

        /// <summary>
        ///     Retrieves the files from the specified directory from the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The username of the user.</param>
        /// <param name="request">The directory contents request.</param>
        /// <returns></returns>
        [HttpPost("{username}/directory")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(IEnumerable<Directory>), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Directory([FromRoute, UrlEncoded, Required] string username, [FromBody, Required] DirectoryContentsRequest request)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (request == null || string.IsNullOrEmpty(request.Directory))
            {
                return BadRequest();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            try
            {
                var result = await Client.GetDirectoryContentsAsync(username, request.Directory);

                Log.Debug("{Endpoint} response from {User} for directory '{Directory}': {@Response}", nameof(Directory), username, request.Directory, result);

                return Ok(result);
            }
            catch (UserOfflineException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        ///     Retrieves information about the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The username of the user.</param>
        /// <returns></returns>
        [HttpGet("{username}/info")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(Info), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Info([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            try
            {
                var response = await Users.GetInfoAsync(username);
                return Ok(response);
            }
            catch (UserOfflineException ex)
            {
                return NotFound(ex.Message);
            }
        }

        /// <summary>
        ///     Retrieves status for the specified <paramref name="username"/>.
        /// </summary>
        /// <param name="username">The username of the user.</param>
        /// <returns></returns>
        [HttpGet("{username}/status")]
        [Authorize(Policy = AuthPolicy.Any, Roles = AuthRole.ReadWriteOrAdministrator)]
        [ProducesResponseType(typeof(Status), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> Status([FromRoute, UrlEncoded, Required] string username)
        {
            if (Program.IsRelayAgent)
            {
                return Forbid();
            }

            if (Users.IsBlacklisted(username))
            {
                return NotFound();
            }

            try
            {
                var response = await Users.GetStatusAsync(username);
                return Ok(response);
            }
            catch (UserOfflineException ex)
            {
                return NotFound(ex.Message);
            }
        }
    }
}