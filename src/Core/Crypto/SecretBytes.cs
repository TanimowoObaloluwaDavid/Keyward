using System;
using System.Runtime.InteropServices;

namespace Keyward.Core.Crypto
{
    /// <summary>
    /// Owns a byte buffer that holds key material or decrypted plaintext.
    /// The buffer is zeroed on <see cref="Dispose"/> and is pinned in the managed heap for
    /// its lifetime so the garbage collector can neither relocate nor collect it while a
    /// cryptographic operation is reading it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pinning does <b>not</b> make plaintext invisible to an attacker who already controls the
    /// machine. A debugger, a full-process memory dump, a hibernation file or a crash report can
    /// still observe the bytes. Pinning only removes the GC-relocation and GC-collection risks.
    /// See docs/SECURITY.md for the full list of documented limitations.
    /// </para>
    /// <para>
    /// This type is deliberately not thread-safe: a single secret is expected to be owned by one
    /// operation at a time. Callers that share a key must marshal access themselves.
    /// </para>
    /// </remarks>
    public sealed class SecretBytes : IDisposable
    {
        private byte[] _buffer;
        private GCHandle _pin;
        private bool _zeroed;

        /// <summary>Takes ownership of <paramref name="initial"/> and pins it.</summary>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="initial"/> is null.</exception>
        public SecretBytes(byte[] initial)
        {
            if (initial == null) throw new ArgumentNullException("initial");
            if (initial.Length == 0) throw new ArgumentException("Secret buffer cannot be empty.", "initial");

            _buffer = initial;
            _pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            _zeroed = false;
        }

        /// <summary>Allocates a zeroed buffer of <paramref name="length"/> bytes and pins it.</summary>
        public SecretBytes(int length)
            : this(new byte[length])
        {
        }

        /// <summary>Length of the secret in bytes.</summary>
        public int Length
        {
            get
            {
                ThrowIfDisposed();
                return _buffer.Length;
            }
        }

        /// <summary>
        /// The pinned array itself. Callers must not mutate it and must not let it escape the
        /// scope of the cryptographic operation that received it.
        /// </summary>
        public byte[] Buffer
        {
            get
            {
                ThrowIfDisposed();
                return _buffer;
            }
        }

        /// <summary>
        /// Returns an unpinned copy. Used when a primitive needs a buffer it can safely resize
        /// or hand to an API that may keep a reference.
        /// </summary>
        public byte[] Copy()
        {
            ThrowIfDisposed();
            byte[] copy = new byte[_buffer.Length];
            // Fully qualified: inside this class the name "Buffer" resolves to the property
            // below, which shadows the System.Buffer static helper class.
            System.Buffer.BlockCopy(_buffer, 0, copy, 0, copy.Length);
            return copy;
        }

        /// <summary>
        /// Overwrites the buffer in place. Used to erase a value as soon as it is no longer
        /// needed, before the owning object is disposed.
        /// </summary>
        public void Wipe()
        {
            if (_zeroed || _buffer == null) return;
            Array.Clear(_buffer, 0, _buffer.Length);
            GC.KeepAlive(_buffer);
        }

        private void ThrowIfDisposed()
        {
            if (_zeroed || _buffer == null)
                throw new ObjectDisposedException("SecretBytes");
        }

        /// <summary>Zeroes the buffer and releases the pinned handle. Safe to call repeatedly.</summary>
        public void Dispose()
        {
            if (_zeroed)
            {
                GC.KeepAlive(this);
                return;
            }

            _zeroed = true;

            if (_buffer != null)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
            }

            if (_pin.IsAllocated)
                _pin.Free();

            _buffer = null;
            GC.KeepAlive(this);
        }
    }
}
