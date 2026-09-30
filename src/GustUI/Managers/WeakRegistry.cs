using System;
using System.Collections.Generic;

namespace GustUI.Managers
{
    /// <summary>
    /// A list of every live object of some kind that does not itself keep
    /// them alive (ezmuze #604).
    ///
    /// The shape a static "every window that exists" registry needs: objects
    /// join when they are made and are meant to leave when they are done, but
    /// any object that is dropped without leaving (built and never shown,
    /// detached without being closed) must still be collectable. A strong
    /// list made the registry the one thing holding such an object, and
    /// through it everything the object could reach.
    ///
    /// Entries whose object has been collected are pruned on every
    /// <see cref="Add"/> and <see cref="Alive"/>, so the list stays the size
    /// of the live set. Not thread-safe: the UI thread's.
    /// </summary>
    internal sealed class WeakRegistry<T>
        where T : class
    {
        private readonly List<WeakReference<T>> entries = new List<WeakReference<T>>();

        /// <summary>How many entries are held, collected ones included until
        /// the next prune.</summary>
        public int EntryCount => entries.Count;

        public void Add(T item)
        {
            entries.RemoveAll(entry => !entry.TryGetTarget(out _));
            entries.Add(new WeakReference<T>(item));
        }

        public void Remove(T item)
        {
            entries.RemoveAll(entry => !entry.TryGetTarget(out T target) || ReferenceEquals(target, item));
        }

        /// <summary>The objects still alive, in the order they joined. A
        /// snapshot, so a caller may add or remove while walking it.</summary>
        public List<T> Alive()
        {
            var alive = new List<T>(entries.Count);
            entries.RemoveAll(entry =>
            {
                if (entry.TryGetTarget(out T item))
                {
                    alive.Add(item);
                    return false;
                }

                return true;
            });
            return alive;
        }
    }
}
