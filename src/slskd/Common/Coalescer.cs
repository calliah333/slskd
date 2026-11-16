// <copyright file="Coalescer.cs" company="JP Dillingham">
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

namespace slskd
{
    using System;
    using System.Threading.Tasks;
    using Serilog;

    /// <summary>
    ///     Coalesces bursts of posted values into at most one delivery per interval.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The first value posted while idle is delivered immediately. Values posted while a delivery is in flight or
    ///         within <see cref="Interval"/> of the previous delivery replace one another, and only the most recent is delivered
    ///         once the interval elapses. The last posted value is therefore always delivered, deliveries never overlap,
    ///         and they occur in the order in which values were posted.
    ///     </para>
    /// </remarks>
    /// <typeparam name="T">The type of the coalesced value.</typeparam>
    public sealed class Coalescer<T>
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Coalescer{T}"/> class.
        /// </summary>
        /// <param name="interval">The minimum interval between deliveries.</param>
        /// <param name="deliver">The delegate that delivers a value.</param>
        public Coalescer(TimeSpan interval, Func<T, Task> deliver)
        {
            Interval = interval;
            Deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        }

        /// <summary>
        ///     Gets the minimum interval between deliveries.
        /// </summary>
        public TimeSpan Interval { get; }

        private Func<T, Task> Deliver { get; }
        private ILogger Log { get; } = Serilog.Log.ForContext<Coalescer<T>>();
        private object SyncRoot { get; } = new object();
        private T Latest { get; set; }
        private bool Pending { get; set; }
        private bool Pumping { get; set; }

        /// <summary>
        ///     Posts the specified <paramref name="value"/> for delivery, replacing any value not yet delivered.
        /// </summary>
        /// <param name="value">The value to deliver.</param>
        public void Post(T value)
        {
            lock (SyncRoot)
            {
                Latest = value;
                Pending = true;

                if (Pumping)
                {
                    return;
                }

                Pumping = true;
            }

            _ = Task.Run(PumpAsync);
        }

        private async Task PumpAsync()
        {
            while (true)
            {
                T value;

                lock (SyncRoot)
                {
                    if (!Pending)
                    {
                        Pumping = false;
                        return;
                    }

                    value = Latest;
                    Latest = default;
                    Pending = false;
                }

                try
                {
                    await Deliver(value);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to deliver coalesced {Type}: {Message}", typeof(T).Name, ex.Message);
                }

                await Task.Delay(Interval);
            }
        }
    }
}
