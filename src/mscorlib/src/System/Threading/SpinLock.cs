// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#pragma warning disable 0420

// =+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+=+
//
// A spin lock is a mutual exclusion lock primitive where a thread trying to acquire the lock waits in a loop ("spins")
// repeatedly checking until the lock becomes available. As the thread remains active performing a non-useful task,
// the use of such a lock is a kind of busy waiting and consumes CPU resources without performing real work. 
//
//
// =-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-=-
using System;
using System.Diagnostics;
using System.Security.Permissions;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Diagnostics.Contracts;

namespace System.Threading
{

    /// <summary>
    /// Provides a mutual exclusion lock primitive where a thread trying to acquire the lock waits in a loop
    /// repeatedly checking until the lock becomes available.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spin locks can be used for leaf-level locks where the object allocation implied by using a <see
    /// cref="System.Threading.Monitor"/>, in size or due to garbage collection pressure, is overly
    /// expensive. Avoiding blocking is another reason that a spin lock can be useful, however if you expect
    /// any significant amount of blocking, you are probably best not using spin locks due to excessive
    /// spinning. Spinning can be beneficial when locks are fine grained and large in number (for example, a
    /// lock per node in a linked list) as well as when lock hold times are always extremely short. In
    /// general, while holding a spin lock, one should avoid blocking, calling anything that itself may
    /// block, holding more than one spin lock at once, making dynamically dispatched calls (interface and
    /// virtuals), making statically dispatched calls into any code one doesn't own, or allocating memory.
    /// </para>
    /// <para>
    /// <see cref="SpinLock"/> should only be used when it's been determined that doing so will improve an
    /// application's performance. It's also important to note that <see cref="SpinLock"/> is a value type,
    /// for performance reasons. As such, one must be very careful not to accidentally copy a SpinLock
    /// instance, as the two instances (the original and the copy) would then be completely independent of
    /// one another, which would likely lead to erroneous behavior of the application. If a SpinLock instance
    /// must be passed around, it should be passed by reference rather than by value.
    /// </para>
    /// <para>
    /// Do not store <see cref="SpinLock"/> instances in readonly fields.
    /// </para>
    /// <para>
    /// All members of <see cref="SpinLock"/> are thread-safe and may be used from multiple threads
    /// concurrently.
    /// </para>
    /// </remarks>
    [ComVisible(false)]
    [HostProtection(Synchronization = true, ExternalThreading = true)]
    [DebuggerTypeProxy(typeof(SystemThreading_SpinLockDebugView))]
    [DebuggerDisplay("IsHeld = {IsHeld}")]
    public struct SpinLock
    {
        // The current ownership state is a single signed int. There are two modes:
        //
        //    1) Ownership tracking enabled: the high bit is 0, and the remaining bits
        //       store the managed thread ID of the current owner.  When the 31 low bits
        //       are 0, the lock is available.
        //    2) Performance mode: when the high bit is 1, lock availability is indicated by the low bit.  
        //       When the low bit is 1 -- the lock is held; 0 -- the lock is available.
        //
        // There are several masks and constants below for convenience.

        private volatile int m_owner;

        // The multiplier factor for the each spinning iteration
        // This number has been chosen after trying different numbers on different CPUs (4, 8 and 16 ) and this provided the best results
        private const int SPINNING_FACTOR = 100;

        // After how many yields, call Sleep(1)
        private const int SLEEP_ONE_FREQUENCY = 40;

        // After how many yields, call Sleep(0)
        private const int SLEEP_ZERO_FREQUENCY = 10;

        // After how many yields, check the timeout
        private const int TIMEOUT_CHECK_FREQUENCY = 10;

        // Thr thread tracking disabled mask
        private const int LOCK_ID_DISABLE_MASK = unchecked((int)0x80000000);        //1000 0000 0000 0000 0000 0000 0000 0000

        //the lock is held by some thread, but we don't know which
        private const int LOCK_ANONYMOUS_OWNED = 0x1;                               //0000 0000 0000 0000 0000 0000 0000 0001

        // Waiters mask if the thread tracking is disabled
        private const int WAITERS_MASK = ~(LOCK_ID_DISABLE_MASK | 1);               //0111 1111 1111 1111 1111 1111 1111 1110

        // The Thread tacking is disabled and the lock bit is set, used in Enter fast path to make sure the id is disabled and lock is available
        private const int ID_DISABLED_AND_ANONYMOUS_OWNED = unchecked((int)0x80000001); //1000 0000 0000 0000 0000 0000 0000 0001

        // If the thread is unowned if:
        // m_owner zero and the threa tracking is enabled
        // m_owner & LOCK_ANONYMOUS_OWNED = zero and the thread tracking is disabled
        private const int LOCK_UNOWNED = 0;

        // The maximum number of waiters (only used if the thread tracking is disabled)
        // The actual maximum waiters count is this number divided by two because each waiter increments the waiters count by 2
        // The waiters count is calculated by m_owner & WAITERS_MASK 01111....110
        private static int MAXIMUM_WAITERS = WAITERS_MASK;


        /// <summary>
        /// Initializes a new instance of the <see cref="T:System.Threading.SpinLock"/>
        /// structure with the option to track thread IDs to improve debugging.
        /// </summary>
        /// <remarks>
        /// The default constructor for <see cref="SpinLock"/> tracks thread ownership.
        /// </remarks>
        /// <param name="enableThreadOwnerTracking">Whether to capture and use thread IDs for debugging
        /// purposes.</param>
        public SpinLock(bool enableThreadOwnerTracking)
        {
            m_owner = LOCK_UNOWNED;
            if (!enableThreadOwnerTracking)
            {
                m_owner |= LOCK_ID_DISABLE_MASK;
                Contract.Assert(!IsThreadOwnerTrackingEnabled, "property should be false by now");
            }
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="T:System.Threading.SpinLock"/>
        /// structure with the option to track thread IDs to improve debugging.
        /// </summary>
        /// <remarks>
        /// The default constructor for <see cref="SpinLock"/> tracks thread ownership.
        /// </remarks>
        /// <summary>
        /// Acquires the lock in a reliable manner, such that even if an exception occurs within the method
        /// call, <paramref name="lockTaken"/> can be examined reliably to determine whether the lock was
        /// acquired.
        /// </summary>
        /// <remarks>
        /// <see cref="SpinLock"/> is a non-reentrant lock, meaning that if a thread holds the lock, it is
        /// not allowed to enter the lock again. If thread ownership tracking is enabled (whether it's
        /// enabled is available through <see cref="IsThreadOwnerTrackingEnabled"/>), an exception will be
        /// thrown when a thread tries to re-enter a lock it already holds. However, if thread ownership
        /// tracking is disabled, attempting to enter a lock already held will result in deadlock.
        /// </remarks>
        /// <param name="lockTaken">True if the lock is acquired; otherwise, false. <paramref
        /// name="lockTaken"/> must be initialized to false prior to calling this method.</param>
        /// <exception cref="T:System.Threading.LockRecursionException">
        /// Thread ownership tracking is enabled, and the current thread has already acquired this lock.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// The <paramref name="lockTaken"/> argument must be initialized to false prior to calling Enter.
        /// </exception>
        public void Enter(ref bool lockTaken)
        {
#if !FEATURE_CORECLR
            Thread.BeginCriticalRegion();
#endif
            //Try to keep the code and branching in this method as small as possible in order to inline the method
            int observedOwner = m_owner;
            if (lockTaken || //invalid parameter
                (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK || //thread tracking is enabled or the lock is already acquired
                Interlocked.CompareExchange(ref m_owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner) //acquiring the lock failed
                ContinueTryEnter(Timeout.Infinite, ref lockTaken); // Then try the slow path if any of the above conditions is met

        }

        /// <summary>
        /// Attempts to acquire the lock in a reliable manner, such that even if an exception occurs within
        /// the method call, <paramref name="lockTaken"/> can be examined reliably to determine whether the
        /// lock was acquired.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Enter"/>, TryEnter will not block waiting for the lock to be available. If the
        /// lock is not available when TryEnter is called, it will return immediately without any further
        /// spinning.
        /// </remarks>
        /// <param name="lockTaken">True if the lock is acquired; otherwise, false. <paramref
        /// name="lockTaken"/> must be initialized to false prior to calling this method.</param>
        /// <exception cref="T:System.Threading.LockRecursionException">
        /// Thread ownership tracking is enabled, and the current thread has already acquired this lock.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// The <paramref name="lockTaken"/> argument must be initialized to false prior to calling TryEnter.
        /// </exception>
        public void TryEnter(ref bool lockTaken)
        {
            TryEnter(0, ref lockTaken);
        }

        /// <summary>
        /// Attempts to acquire the lock in a reliable manner, such that even if an exception occurs within
        /// the method call, <paramref name="lockTaken"/> can be examined reliably to determine whether the
        /// lock was acquired.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Enter"/>, TryEnter will not block indefinitely waiting for the lock to be
        /// available. It will block until either the lock is available or until the <paramref
        /// name="timeout"/>
        /// has expired.
        /// </remarks>
        /// <param name="timeout">A <see cref="System.TimeSpan"/> that represents the number of milliseconds
        /// to wait, or a <see cref="System.TimeSpan"/> that represents -1 milliseconds to wait indefinitely.
        /// </param>
        /// <param name="lockTaken">True if the lock is acquired; otherwise, false. <paramref
        /// name="lockTaken"/> must be initialized to false prior to calling this method.</param>
        /// <exception cref="T:System.Threading.LockRecursionException">
        /// Thread ownership tracking is enabled, and the current thread has already acquired this lock.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// The <paramref name="lockTaken"/> argument must be initialized to false prior to calling TryEnter.
        /// </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="timeout"/> is a negative
        /// number other than -1 milliseconds, which represents an infinite time-out -or- timeout is greater
        /// than <see cref="System.Int32.MaxValue"/> milliseconds.
        /// </exception>
        public void TryEnter(TimeSpan timeout, ref bool lockTaken)
        {
            // Validate the timeout
            Int64 totalMilliseconds = (Int64)timeout.TotalMilliseconds;
            if (totalMilliseconds < -1 || totalMilliseconds > int.MaxValue)
            {
                throw new System.ArgumentOutOfRangeException(
                    "timeout", timeout, Environment.GetResourceString("SpinLock_TryEnter_ArgumentOutOfRange"));
            }

            // Call reliable enter with the int-based timeout milliseconds
            TryEnter((int)timeout.TotalMilliseconds, ref lockTaken);
        }

        /// <summary>
        /// Attempts to acquire the lock in a reliable manner, such that even if an exception occurs within
        /// the method call, <paramref name="lockTaken"/> can be examined reliably to determine whether the
        /// lock was acquired.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Enter"/>, TryEnter will not block indefinitely waiting for the lock to be
        /// available. It will block until either the lock is available or until the <paramref
        /// name="millisecondsTimeout"/> has expired.
        /// </remarks>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or <see
        /// cref="System.Threading.Timeout.Infinite"/> (-1) to wait indefinitely.</param>
        /// <param name="lockTaken">True if the lock is acquired; otherwise, false. <paramref
        /// name="lockTaken"/> must be initialized to false prior to calling this method.</param>
        /// <exception cref="T:System.Threading.LockRecursionException">
        /// Thread ownership tracking is enabled, and the current thread has already acquired this lock.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// The <paramref name="lockTaken"/> argument must be initialized to false prior to calling TryEnter.
        /// </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException"><paramref name="millisecondsTimeout"/> is
        /// a negative number other than -1, which represents an infinite time-out.</exception>
        public void TryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
#if !FEATURE_CORECLR
            Thread.BeginCriticalRegion();
#endif

            int observedOwner = m_owner;
            if (millisecondsTimeout < -1 || //invalid parameter
                lockTaken || //invalid parameter
                (observedOwner & ID_DISABLED_AND_ANONYMOUS_OWNED) != LOCK_ID_DISABLE_MASK ||  //thread tracking is enabled or the lock is already acquired
                Interlocked.CompareExchange(ref m_owner, observedOwner | LOCK_ANONYMOUS_OWNED, observedOwner, ref lockTaken) != observedOwner) // acquiring the lock failed
                ContinueTryEnter(millisecondsTimeout, ref lockTaken); // The call the slow pth
        }

        /// <summary>
        /// Try acquire the lock with long path, this is usually called after the first path in Enter and
        /// TryEnter failed The reason for short path is to make it inline in the run time which improves the
        /// performance. This method assumed that the parameter are validated in Enter ir TryENter method
        /// </summary>
        /// <param name="millisecondsTimeout">The timeout milliseconds</param>
        /// <param name="lockTaken">The lockTaken param</param>
        private void ContinueTryEnter(int millisecondsTimeout, ref bool lockTaken)
        {
            //Leave the critical region which is entered by the fast path
#if !FEATURE_CORECLR
            Thread.EndCriticalRegion();
#endif
            // The fast path doesn't throw any exception, so we have to validate the parameters here
            if (lockTaken)
            {
                lockTaken = false;
                throw new System.ArgumentException(Environment.GetResourceString("SpinLock_TryReliableEnter_ArgumentException"));
            }

            if (millisecondsTimeout < -1)
            {
                throw new ArgumentOutOfRangeException(
                    "millisecondsTimeout", millisecondsTimeout, Environment.GetResourceString("SpinLock_TryEnter_ArgumentOutOfRange"));
            }


            uint startTime = 0;
            if (millisecondsTimeout != Timeout.Infinite && millisecondsTimeout != 0)
            {
                startTime = TimeoutHelper.GetTime();
            }

#if !FEATURE_CORECLR
            if (CdsSyncEtwBCLProvider.Log.IsEnabled())
            {
                CdsSyncEtwBCLProvider.Log.SpinLock_FastPathFailed(m_owner);
            }
#endif

            if (IsThreadOwnerTrackingEnabled)
            {
                // Slow path for enabled thread tracking mode
                ContinueTryEnterWithThreadTracking(millisecondsTimeout, startTime, ref lockTaken);
                return;
            }

            // then thread tracking is disabled
            // In this case there are three ways to acquire the lock
            // 1- the first way the thread either tries to get the lock if it's free or updates the waiters, if the turn >= the processors count then go to 3 else go to 2
            // 2- In this step the waiter threads spins and tries to acquire the lock, the number of spin iterations and spin count is dependent on the thread turn
            // the late the thread arrives the more it spins and less frequent it check the lock avilability
            // Also the spins count is increases each iteration
            // If the spins iterations finished and failed to acquire the lock, go to step 3
            // 3- This is the yielding step, there are two ways of yielding Thread.Yield and Sleep(1)
            // If the timeout is expired in after step 1, we need to decrement the waiters count before returning

            int observedOwner;
            int turn = int.MaxValue;
            //***Step 1, take the lock or update the waiters

            // try to acquire the lock directly if possible or update the waiters count
            observedOwner = m_owner;
            if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
            {
#if !FEATURE_CORECLR
                Thread.BeginCriticalRegion();
#endif

                if (Interlocked.CompareExchange(ref m_owner, observedOwner | 1, observedOwner, ref lockTaken) == observedOwner)
                {
                    return;
                }

#if !FEATURE_CORECLR
                Thread.EndCriticalRegion();
#endif
            }
            else //failed to acquire the lock,then try to update the waiters. If the waiters count reached the maximum, jsut break the loop to avoid overflow
            {
                if ((observedOwner & WAITERS_MASK) != MAXIMUM_WAITERS)
                    turn = (Interlocked.Add(ref m_owner, 2) & WAITERS_MASK) >> 1 ;
            }



            // Check the timeout.
            if (millisecondsTimeout == 0 ||
                (millisecondsTimeout != Timeout.Infinite &&
                TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout) <= 0))
            {
                DecrementWaiters();
                return;
            }

            //***Step 2. Spinning
            //lock acquired failed and waiters updated
            int processorCount = PlatformHelper.ProcessorCount;
            if (turn < processorCount)
            {
                int processFactor = 1;
                for (int i = 1; i <= turn * SPINNING_FACTOR; i++)
                {
                    Thread.SpinWait((turn + i) * SPINNING_FACTOR * processFactor);
                    if (processFactor < processorCount)
                        processFactor++;
                    observedOwner = m_owner;
                    if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
                    {
#if !FEATURE_CORECLR
                        Thread.BeginCriticalRegion();
#endif

                        int newOwner = (observedOwner & WAITERS_MASK) == 0 ? // Gets the number of waiters, if zero
                            observedOwner | 1 // don't decrement it. just set the lock bit, it is zzero because a previous call of Exit(false) ehich corrupted the waiters
                            : (observedOwner - 2) | 1; // otherwise decrement the waiters and set the lock bit
                        Contract.Assert((newOwner & WAITERS_MASK) >= 0);

                        if (Interlocked.CompareExchange(ref m_owner, newOwner, observedOwner, ref lockTaken) == observedOwner)
                        {
                            return;
                        }

#if !FEATURE_CORECLR
                        Thread.EndCriticalRegion();
#endif
                    }
                }
            }

            // Check the timeout.
            if (millisecondsTimeout != Timeout.Infinite && TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout) <= 0)
            {
                DecrementWaiters();
                return;
            }

            //*** Step 3, Yielding
            //Sleep(1) every 50 yields
            int yieldsoFar = 0;
            while (true)
            {
                observedOwner = m_owner;
                if ((observedOwner & LOCK_ANONYMOUS_OWNED) == LOCK_UNOWNED)
                {
#if !FEATURE_CORECLR
                    Thread.BeginCriticalRegion();
#endif
                    int newOwner = (observedOwner & WAITERS_MASK) == 0 ? // Gets the number of waiters, if zero
                           observedOwner | 1 // don't decrement it. just set the lock bit, it is zzero because a previous call of Exit(false) ehich corrupted the waiters
                           : (observedOwner - 2) | 1; // otherwise decrement the waiters and set the lock bit
                    Contract.Assert((newOwner & WAITERS_MASK) >= 0);

                    if (Interlocked.CompareExchange(ref m_owner, newOwner, observedOwner, ref lockTaken) == observedOwner)
                    {
                        return;
                    }

#if !FEATURE_CORECLR
                    Thread.EndCriticalRegion();
#endif
                }

                if (yieldsoFar % SLEEP_ONE_FREQUENCY == 0)
                {
                    Thread.Sleep(1);
                }
                else if (yieldsoFar % SLEEP_ZERO_FREQUENCY == 0)
                {
                    Thread.Sleep(0);
                }
                else
                {
                    Thread.Yield();
                }

                if (yieldsoFar % TIMEOUT_CHECK_FREQUENCY == 0)
                {
                    //Check the timeout.
                    if (millisecondsTimeout != Timeout.Infinite && TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout) <= 0)
                    {
                        DecrementWaiters();
                        return;
                    }
                }

                yieldsoFar++;
            }
        }

        /// <summary>
        /// decrements the waiters, in case of the timeout is expired
        /// </summary>
        private void DecrementWaiters()
        {
            SpinWait spinner = new SpinWait();
            while (true)
            {
                int observedOwner = m_owner;
                if ((observedOwner & WAITERS_MASK) == 0) return; // don't decrement the waiters if it's corrupted by previous call of Exit(false)
                if (Interlocked.CompareExchange(ref m_owner, observedOwner - 2, observedOwner) == observedOwner)
                {
                    Contract.Assert(!IsThreadOwnerTrackingEnabled); // Make sure the waiters never be negative which will cause the thread tracking bit to be flipped
                    break;
                }
                spinner.SpinOnce();
            }

        }

        /// <summary>
        /// ContinueTryEnter for the thread tracking mode enabled
        /// </summary>
        private void ContinueTryEnterWithThreadTracking(int millisecondsTimeout, uint startTime, ref bool lockTaken)
        {
            Contract.Assert(IsThreadOwnerTrackingEnabled);

            int lockUnowned = 0;
            // We are using thread IDs to mark ownership. Snap the thread ID and check for recursion.
            // We also must or the ID enablement bit, to ensure we propagate when we CAS it in.
            int m_newOwner = Thread.CurrentThread.ManagedThreadId;
            if (m_owner == m_newOwner)
            {
                // We don't allow lock recursion.
                throw new LockRecursionException(Environment.GetResourceString("SpinLock_TryEnter_LockRecursionException"));
            }


            SpinWait spinner = new SpinWait();

            // Loop until the lock has been successfully acquired or, if specified, the timeout expires.
            do
            {

                // We failed to get the lock, either from the fast route or the last iteration
                // and the timeout hasn't expired; spin once and try again.
                spinner.SpinOnce();

                // Test before trying to CAS, to avoid acquiring the line exclusively unnecessarily.

                if (m_owner == lockUnowned)
                {
#if !FEATURE_CORECLR
                    Thread.BeginCriticalRegion();
#endif
                    if (Interlocked.CompareExchange(ref m_owner, m_newOwner, lockUnowned, ref lockTaken) == lockUnowned)
                    {
                        return;
                    }
#if !FEATURE_CORECLR
                    // The thread failed to get the lock, so we don't need to remain in a critical region.
                    Thread.EndCriticalRegion();
#endif
                }
                // Check the timeout.  We only RDTSC if the next spin will yield, to amortize the cost.
                if (millisecondsTimeout == 0 ||
                    (millisecondsTimeout != Timeout.Infinite && spinner.NextSpinWillYield &&
                    TimeoutHelper.UpdateTimeOut(startTime, millisecondsTimeout) <= 0))
                {
                    return;
                }
            } while (true);
        }

        /// <summary>
        /// Releases the lock.
        /// </summary>
        /// <remarks>
        /// The default overload of <see cref="Exit()"/> provides the same behavior as if calling <see
        /// cref="Exit(Boolean)"/> using true as the argument, but Exit() could be slightly faster than Exit(true).
        /// </remarks>
        /// <exception cref="SynchronizationLockException">
        /// Thread ownership tracking is enabled, and the current thread is not the owner of this lock.
        /// </exception>
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        public void Exit()
        {
            //This is the fast path for the thread tracking is disabled, otherwise go to the slow path
            if ((m_owner & LOCK_ID_DISABLE_MASK) == 0)
                ExitSlowPath(true);
            else
                Interlocked.Decrement(ref m_owner);

#if !FEATURE_CORECLR
            Thread.EndCriticalRegion();
#endif

        }

        /// <summary>
        /// Releases the lock.
        /// </summary>
        /// <param name="useMemoryBarrier">
        /// A Boolean value that indicates whether a memory fence should be issued in order to immediately
        /// publish the exit operation to other threads.
        /// </param>
        /// <remarks>
        /// Calling <see cref="Exit(Boolean)"/> with the <paramref name="useMemoryBarrier"/> argument set to
        /// true will improve the fairness of the lock at the expense of some performance. The default <see
        /// cref="Enter"/>
        /// overload behaves as if specifying true for <paramref name="useMemoryBarrier"/>.
        /// </remarks>
        /// <exception cref="SynchronizationLockException">
        /// Thread ownership tracking is enabled, and the current thread is not the owner of this lock.
        /// </exception>
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        public void Exit(bool useMemoryBarrier)
        {
            // This is the fast path for the thread tracking is diabled and not to use memory barrier, otherwise go to the slow path
            // The reason not to add else statement if the usememorybarrier is that it will add more barnching in the code and will prevent
            // method inlining, so this is optimized for useMemoryBarrier=false and Exit() overload optimized for useMemoryBarrier=true
            if ((m_owner & LOCK_ID_DISABLE_MASK) != 0 && !useMemoryBarrier)
            {
                int tmpOwner = m_owner;
                m_owner = tmpOwner & (~LOCK_ANONYMOUS_OWNED);
            }
            else
                ExitSlowPath(useMemoryBarrier);

#if !FEATURE_CORECLR
            Thread.EndCriticalRegion();
#endif
        }

        /// <summary>
        /// The slow path for exit method if the fast path failed
        /// </summary>
        /// <param name="useMemoryBarrier">
        /// A Boolean value that indicates whether a memory fence should be issued in order to immediately
        /// publish the exit operation to other threads
        /// </param>
        private void ExitSlowPath(bool useMemoryBarrier)
        {
            bool threadTrackingEnabled = (m_owner & LOCK_ID_DISABLE_MASK) == 0;
            if (threadTrackingEnabled && !IsHeldByCurrentThread)
            {
                throw new System.Threading.SynchronizationLockException(
                    Environment.GetResourceString("SpinLock_Exit_SynchronizationLockException"));
            }

            if (useMemoryBarrier)
            {
                if (threadTrackingEnabled)
                    Interlocked.Exchange(ref m_owner, LOCK_UNOWNED);
                else
                    Interlocked.Decrement(ref m_owner);

            }
            else
            {
                if (threadTrackingEnabled)
                    m_owner = LOCK_UNOWNED;
                else
                {
                    int tmpOwner = m_owner;
                    m_owner = tmpOwner & (~LOCK_ANONYMOUS_OWNED);
                }

            }

        }

        /// <summary>
        /// Gets whether the lock is currently held by any thread.
        /// </summary>
        public bool IsHeld
        {
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            get
            {
                if (IsThreadOwnerTrackingEnabled)
                    return m_owner != LOCK_UNOWNED;

                return (m_owner & LOCK_ANONYMOUS_OWNED) != LOCK_UNOWNED;
            }
        }

        /// <summary>
        /// Gets whether the lock is currently held by any thread.
        /// </summary>
        /// <summary>
        /// Gets whether the lock is held by the current thread.
        /// </summary>
        /// <remarks>
        /// If the lock was initialized to track owner threads, this will return whether the lock is acquired
        /// by the current thread. It is invalid to use this property when the lock was initialized to not
        /// track thread ownership.
        /// </remarks>
        /// <exception cref="T:System.InvalidOperationException">
        /// Thread ownership tracking is disabled.
        /// </exception>
        public bool IsHeldByCurrentThread
        {
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            get
            {
                if (!IsThreadOwnerTrackingEnabled)
                {
                    throw new InvalidOperationException(Environment.GetResourceString("SpinLock_IsHeldByCurrentThread"));
                }
                return ((m_owner & (~LOCK_ID_DISABLE_MASK)) == Thread.CurrentThread.ManagedThreadId);
            }
        }

        /// <summary>Gets whether thread ownership tracking is enabled for this instance.</summary>
        public bool IsThreadOwnerTrackingEnabled
        {
            [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
            get { return (m_owner & LOCK_ID_DISABLE_MASK) == 0; }
        }

        #region Debugger proxy class
        /// <summary>
        /// Internal class used by debug type proxy attribute to display the owner thread ID 
        /// </summary>
        internal class SystemThreading_SpinLockDebugView
        {
            // SpinLock object
            private SpinLock m_spinLock;

            /// <summary>
            /// SystemThreading_SpinLockDebugView constructor
            /// </summary>
            /// <param name="spinLock">The SpinLock to be proxied.</param>
            public SystemThreading_SpinLockDebugView(SpinLock spinLock)
            {
                // Note that this makes a copy of the SpinLock (struct). It doesn't hold a reference to it.
                m_spinLock = spinLock;
            }

            /// <summary>
            /// Checks if the lock is held by the current thread or not
            /// </summary>
            public bool? IsHeldByCurrentThread
            {
                get
                {
                    try
                    {
                        return m_spinLock.IsHeldByCurrentThread;
                    }
                    catch (InvalidOperationException)
                    {
                        return null;
                    }
                }
            }

            /// <summary>
            /// Gets the current owner thread, zero if it is released
            /// </summary>
            public int? OwnerThreadID
            {
                get
                {
                    if (m_spinLock.IsThreadOwnerTrackingEnabled)
                    {
                        return m_spinLock.m_owner;
                    }
                    else
                    {
                        return null;
                    }
                }
            }


            /// <summary>
            ///  Gets whether the lock is currently held by any thread or not.
            /// </summary>
            public bool IsHeld
            {
                get { return m_spinLock.IsHeld; }
            }
        }
        #endregion

    }
}
#pragma warning restore 0420
