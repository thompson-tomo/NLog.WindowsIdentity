// 
// Copyright (c) 2004-2021 Jaroslaw Kowalski <jaak@jkowalski.net>, Kim Christensen, Julian Verdurmen
// 
// All rights reserved.
// 
// Redistribution and use in source and binary forms, with or without 
// modification, are permitted provided that the following conditions 
// are met:
// 
// * Redistributions of source code must retain the above copyright notice, 
//   this list of conditions and the following disclaimer. 
// 
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution. 
// 
// * Neither the name of Jaroslaw Kowalski nor the names of its 
//   contributors may be used to endorse or promote products derived from this
//   software without specific prior written permission. 
// 
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE 
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE 
// ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE 
// LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR 
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
// SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
// INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN 
// CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) 
// ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF 
// THE POSSIBILITY OF SUCH DAMAGE.
// 

namespace NLog.Targets.Wrappers
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Security;
    using System.Security.Principal;
    using NLog.Common;
    using NLog.Internal;
    using NLog.Layouts;

    /// <summary>
    /// Impersonates another user for the duration of the write.
    /// </summary>
    /// <remarks>
    /// <a href="https://github.com/nlog/nlog/wiki/ImpersonatingWrapper-target">See NLog Wiki</a>
    /// </remarks>
    /// <seealso href="https://github.com/nlog/nlog/wiki/ImpersonatingWrapper-target">Documentation on NLog Wiki</seealso>
    [SecuritySafeCritical]
    [Target("ImpersonatingWrapper", IsWrapper = true)]
    public class ImpersonatingTargetWrapper : WrapperTargetBase
    {
        private readonly Action<AsyncLogEventInfo> _writeLogEvent;
        private readonly Action<IList<AsyncLogEventInfo>> _writeLogEvents;
        private NewIdentityHandle? _newIdentity;

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpersonatingTargetWrapper" /> class.
        /// </summary>
        public ImpersonatingTargetWrapper()
        {
            _writeLogEvent = (l) => WrappedTarget?.WriteAsyncLogEvent(l);
            _writeLogEvents = (l) => WrappedTarget?.WriteAsyncLogEvents(l);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpersonatingTargetWrapper" /> class.
        /// </summary>
        /// <param name="name">Name of the target.</param>
        /// <param name="wrappedTarget">The wrapped target.</param>
        public ImpersonatingTargetWrapper(string name, Target wrappedTarget)
            : this(wrappedTarget)
        {
            Name = name;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ImpersonatingTargetWrapper" /> class.
        /// </summary>
        /// <param name="wrappedTarget">The wrapped target.</param>
        public ImpersonatingTargetWrapper(Target wrappedTarget)
            : this()
        {
            WrappedTarget = wrappedTarget;
        }

        /// <summary>
        /// Gets or sets username to change context to.
        /// </summary>
        /// <remarks>
        /// If you use the user principal name (UPN) format, User@DNSDomainName, the <see cref="Domain"/> must be NULL.
        /// </remarks>
        /// <docgen category='Impersonation Options' order='10' />
        public Layout? UserName { get; set; }

        /// <summary>
        /// Gets or sets the user account password.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public Layout? Password { get; set; }

        /// <summary>
        /// Gets or sets Windows domain name to change context to.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public Layout Domain { get; set; } = ".";

        /// <summary>
        /// Gets or sets the Logon Type.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public SecurityLogOnType LogOnType { get; set; } = SecurityLogOnType.Interactive;

        /// <summary>
        /// Gets or sets the type of the logon provider.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public LogOnProviderType LogOnProvider { get; set; } = LogOnProviderType.Default;

        /// <summary>
        /// Gets or sets the required impersonation level.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public SecurityImpersonationLevel ImpersonationLevel { get; set; } = SecurityImpersonationLevel.Impersonation;

        /// <summary>
        /// Gets or sets a value indicating whether to revert to the credentials of the process instead of impersonating another user.
        /// </summary>
        /// <docgen category='Impersonation Options' order='10' />
        public bool RevertToSelf { get; set; }

        /// <summary>
        /// Initializes the impersonation context.
        /// </summary>
        protected override void InitializeTarget()
        {
            if (!RevertToSelf)
            {
                _newIdentity = ResolveNewIdentity(LogEventInfo.CreateNullEvent(), false);
            }

            base.InitializeTarget();
        }

        private NewIdentityHandle? ResolveNewIdentity(LogEventInfo logEvent, bool forceCreate)
        {
            if (RevertToSelf)
            {
                return null;
            }

            var userName = RenderLogEvent(UserName, logEvent);
            var domain = RenderLogEvent(Domain, logEvent);
            var password = RenderLogEvent(Password, logEvent);

            var currentIdentity = _newIdentity;
            if (currentIdentity != null)
            {
                if (currentIdentity.IsValid(userName, domain, password))
                {
                    return currentIdentity;
                }

                _newIdentity = null;
                currentIdentity.Dispose();
            }

            if (forceCreate || (!string.IsNullOrEmpty(userName) && !string.IsNullOrEmpty(password)))
            {
                return _newIdentity = new NewIdentityHandle(userName, domain, password, LogOnType, LogOnProvider, ImpersonationLevel);
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Closes the impersonation context.
        /// </summary>
        protected override void CloseTarget()
        {
            base.CloseTarget();

            _newIdentity?.Close();
            _newIdentity = null;
        }

        /// <summary>
        /// Changes the security context, forwards the call to the <see cref="WrapperTargetBase.WrappedTarget"/>.Write()
        /// and switches the context back to original.
        /// </summary>
        /// <param name="logEvent">The log event.</param>
        protected override void Write(AsyncLogEventInfo logEvent)
        {
            var newIdentity = ResolveNewIdentity(logEvent.LogEvent, true);
            RunImpersonated(newIdentity, _writeLogEvent, logEvent);
        }

        /// <summary>
        /// Changes the security context, forwards the call to the <see cref="WrapperTargetBase.WrappedTarget"/>.Write()
        /// and switches the context back to original.
        /// </summary>
        /// <param name="logEvents">Log events.</param>
        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            if (logEvents.Count > 0)
            {
                var firstLogEvent = logEvents[0].LogEvent;
                var newIdentity = ResolveNewIdentity(firstLogEvent, true);
                RunImpersonated(newIdentity, _writeLogEvents, logEvents);
            }
        }

        /// <summary>
        /// Flush any pending log messages (in case of asynchronous targets).
        /// </summary>
        /// <param name="asyncContinuation">The asynchronous continuation.</param>
        protected override void FlushAsync(AsyncContinuation asyncContinuation)
        {
            var newIdentity = ResolveNewIdentity(LogEventInfo.CreateNullEvent(), true);
            RunImpersonated(newIdentity, (s) => WrappedTarget?.Flush(s), asyncContinuation);
        }

        private static void RunImpersonated<T>(NewIdentityHandle? newIdentity, Action<T> executeOperation, T state)
        {
            NewIdentityHandle.RunImpersonated(newIdentity, executeOperation, state);
        }

        internal sealed class NewIdentityHandle : IDisposable
        {
            public string UserName { get; }
            public string Domain { get; }
            public int Password { get; }

#if NETFRAMEWORK
            public WindowsIdentity Handle { get; }
            private readonly IntPtr _handle = IntPtr.Zero;
#else
            public Microsoft.Win32.SafeHandles.SafeAccessTokenHandle Handle { get; }
#endif

            public NewIdentityHandle(string userName, string domain, string password, SecurityLogOnType logOnType, LogOnProviderType logOnProvider, SecurityImpersonationLevel impersonationLevel)
            {
                UserName = userName;
                Domain = domain;
                Password = password?.GetHashCode() ?? 0;

#pragma warning disable CS8604 // Possible null reference argument.
                if (!NativeMethods.LogonUser(
                    userName,
                    domain,
                    password,
                    (int)logOnType,
                    (int)logOnProvider,
                    out var logonHandle))
                {
                    Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
                }
#pragma warning restore CS8604 // Possible null reference argument.

#if NETFRAMEWORK
                // adapted from:
                // https://www.codeproject.com/csharp/cpimpersonation1.asp
                if (!NativeMethods.DuplicateToken(logonHandle, (int)impersonationLevel, out _handle))
                {
                    NativeMethods.CloseHandle(logonHandle);
                    throw Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error());
                }

                NativeMethods.CloseHandle(logonHandle);

                // create new identity using new primary token)
                Handle = new WindowsIdentity(_handle);
#else
                Handle = logonHandle;
#endif
            }

            public bool IsValid(string userName, string domain, string password)
            {
                return string.Equals(UserName, userName) && string.Equals(Domain, domain) && Password == (password?.GetHashCode() ?? 0);
            }

            public void Close()
            {
                Handle.Dispose();
#if NETFRAMEWORK
                if (_handle != IntPtr.Zero)
                    NativeMethods.CloseHandle(_handle);
#endif
            }

            public void Dispose()
            {
                Close();
            }

            internal static void RunImpersonated<T>(NewIdentityHandle? newIdentity, Action<T> executeOperation, T state)
            {
#if NETFRAMEWORK
                WindowsImpersonationContext? context = null;
                try
                {
                    context = newIdentity?.Handle.Impersonate() ?? WindowsIdentity.Impersonate(IntPtr.Zero);
                    executeOperation.Invoke(state);
                }
                finally
                {
                    context?.Undo();
                }
#else
                WindowsIdentity.RunImpersonated(newIdentity?.Handle ?? Microsoft.Win32.SafeHandles.SafeAccessTokenHandle.InvalidHandle, () => executeOperation.Invoke(state));
#endif
            }
        }
    }
}