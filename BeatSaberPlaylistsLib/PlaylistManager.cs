﻿using BeatSaberPlaylistsLib.Legacy;
using BeatSaberPlaylistsLib.Types;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BeatSaberPlaylistsLib
{
    /// <summary>
    /// Class that manages <see cref="IPlaylistHandler"/>s and <see cref="IPlaylist"/>s.
    /// </summary>
    public class PlaylistManager
    {
        /// <summary>
        /// Reference to the default <see cref="PlaylistManager"/> which uses the 'Playlists' directory in the current working directory.
        /// </summary>
        public static readonly PlaylistManager DefaultManager = new PlaylistManager("Playlists");
        /// <summary>
        /// Dictionary of <see cref="IPlaylist"/> <see cref="Type"/>s and their associated <see cref="IPlaylistHandler"/>.
        /// </summary>
        protected readonly Dictionary<Type, IPlaylistHandler> PlaylistHandlers = new Dictionary<Type, IPlaylistHandler>();
        /// <summary>
        /// Dictionary of file extensions (uppercase) and their associated <see cref="IPlaylistHandler"/>.
        /// </summary>
        protected readonly Dictionary<string, IPlaylistHandler> PlaylistExtensionHandlers = new Dictionary<string, IPlaylistHandler>();
        /// <summary>
        /// Lock object used when modifying <see cref="ChangedPlaylists"/>.
        /// </summary>
        protected object _changedLock = new object();
        /// <summary>
        /// List of <see cref="IPlaylist"/>s that are marked as changed.
        /// </summary>
        protected readonly HashSet<IPlaylist> ChangedPlaylists = new HashSet<IPlaylist>();
        /// <summary>
        /// Key is the file name in uppercase.
        /// </summary>
        protected ConcurrentDictionary<string, IPlaylist> LoadedPlaylists = new ConcurrentDictionary<string, IPlaylist>();
        /// <summary>
        /// Path to the directory the <see cref="PlaylistManager"/> loads and stores playlists.
        /// </summary>
        public string PlaylistPath { get; protected set; }
        /// <summary>
        /// The default <see cref="IPlaylistHandler"/> for this <see cref="PlaylistManager"/>.
        /// </summary>
        public IPlaylistHandler DefaultHandler { get; } = new LegacyPlaylistHandler();

        /// <summary>
        /// Creates a new <see cref="PlaylistManager"/> to manage playlists in <paramref name="playlistDirectory"/>.
        /// </summary>
        /// <param name="playlistDirectory"></param>
        public PlaylistManager(string playlistDirectory)
            : this(playlistDirectory, new LegacyPlaylistHandler())
        {  }

        /// <summary>
        /// Creates a new <see cref="PlaylistManager"/> to manage playlists in <paramref name="playlistDirectory"/>
        /// and sets the default <see cref="IPlaylistHandler"/> to <paramref name="defaultHandler"/>.
        /// </summary>
        /// <param name="playlistDirectory"></param>
        /// <param name="defaultHandler"></param>
        public PlaylistManager(string playlistDirectory, IPlaylistHandler defaultHandler)
        {
            if (string.IsNullOrEmpty(playlistDirectory))
                throw new ArgumentNullException(nameof(playlistDirectory), $"PlaylistManager cannot have a null {nameof(playlistDirectory)}");
            PlaylistPath = Path.GetFullPath(playlistDirectory);
            Directory.CreateDirectory(PlaylistPath);
            RegisterHandler(defaultHandler);
        }

        /// <summary>
        /// Attempts to register the given <see cref="IPlaylistHandler"/> with the <see cref="PlaylistManager"/>.
        /// Returns false if a handler with the same <see cref="IPlaylistHandler.HandledType"/> already exists,
        /// or if all extensions supported by <paramref name="playlistHandler"/> are already handled.
        /// </summary>
        /// <param name="playlistHandler"></param>
        /// <returns></returns>
        public bool RegisterHandler(IPlaylistHandler playlistHandler)
        {
            bool successful = false;
            if (!PlaylistHandlers.ContainsKey(playlistHandler.HandledType))
            {
                PlaylistHandlers.Add(playlistHandler.HandledType, playlistHandler);
                foreach (string ext in playlistHandler.GetSupportedExtensions())
                {
                    if (!PlaylistExtensionHandlers.ContainsKey(ext))
                    {
                        PlaylistExtensionHandlers.Add(ext, playlistHandler);
                        successful = true;
                    }
                }
            }
            return successful;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="extension"></param>
        /// <param name="playlistHandler"></param>
        public bool RegisterHandlerForExtension(string extension, IPlaylistHandler playlistHandler)
        {
            extension = extension.TrimStart('.');
            bool successful = false;
            if (!PlaylistHandlers.ContainsKey(playlistHandler.HandledType)) // TODO: Should I bother with this check?
            {
                PlaylistHandlers.Add(playlistHandler.HandledType, playlistHandler);
                if (!PlaylistExtensionHandlers.ContainsKey(extension))
                    PlaylistExtensionHandlers.Add(extension, playlistHandler);
                else
                    PlaylistExtensionHandlers[extension] = playlistHandler;
                successful = true;
            }
            return successful;
        }

        /// <summary>
        /// Gets an <see cref="IPlaylistHandler"/> registered for the given <paramref name="extension"/>.
        /// Returns null if no registered handler supports the <paramref name="extension"/>.
        /// </summary>
        /// <param name="extension"></param>
        /// <returns></returns>
        public IPlaylistHandler? GetHandlerForExtension(string extension)
        {
            extension = extension.TrimStart('.').ToUpper();
            PlaylistExtensionHandlers.TryGetValue(extension, out IPlaylistHandler? handler);
            return handler;
        }

        /// <summary>
        /// Creates a new <see cref="IPlaylist"/> using the given parameters.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="title"></param>
        /// <param name="author"></param>
        /// <param name="imageLoader"></param>
        /// <param name="description"></param>
        /// <returns></returns>
        public IPlaylist CreatePlaylist(string fileName, string title, string author, Lazy<string> imageLoader, string? description = null)
        {
            IPlaylist playlist = new LegacyPlaylist(fileName, title, author, imageLoader) { Description = description };
            return playlist;
        }

        /// <summary>
        /// Creates a new <see cref="IPlaylist"/> using the given parameters.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="title"></param>
        /// <param name="author"></param>
        /// <param name="coverImage"></param>
        /// <param name="description"></param>
        /// <returns></returns>
        public IPlaylist CreatePlaylist(string fileName, string title, string author, string coverImage, string? description = null)
        {
            IPlaylist playlist = new LegacyPlaylist(fileName, title, author, coverImage) { Description = description };
            return playlist;
        }

        /// <summary>
        /// Attempts to remove the song with the matching hash from all loaded playlists.
        /// </summary>
        /// <param name="hash"></param>
        public void RemoveSongFromAll(string hash)
        {
            hash = hash.ToUpper();
            IPlaylist[] loadedPlaylists = LoadedPlaylists.Values.ToArray();
            foreach (IPlaylist? playlist in loadedPlaylists)
            {
                if (playlist == null)
                    continue;
                playlist.TryRemoveByHash(hash);
            }
        }

        /// <summary>
        /// Attempts to remove the song from all loaded playlists.
        /// </summary>
        /// <param name="song"></param>
        public void RemoveSongFromAll(IPlaylistSong song)
        {
            string? hash = song.Hash;
            if (hash == null)
                return;
            RemoveSongFromAll(hash);
        }

        /// <summary>
        /// Writes all <see cref="IPlaylist"/>s that have been marked as changed to file.
        /// </summary>
        public void StoreAllPlaylists()
        {
            IPlaylist[]? changedPlaylists;
            lock (_changedLock)
            {
                changedPlaylists = ChangedPlaylists.ToArray();
                ChangedPlaylists.Clear();
            }
            foreach (IPlaylist? playlist in changedPlaylists)
            {
                if (playlist == null)
                    continue;
                StorePlaylist(playlist, false);
            }
        }

        private void OnPlaylistChanged(object sender, EventArgs e)
        {
            if (sender is IPlaylist playlist)
            {
                MarkPlaylistChanged(playlist);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="playlist"></param>
        /// <param name="removeFromChanged"></param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="playlist"/> is null.</exception>
        /// <exception cref="PlaylistSerializationException">Thrown on serialization errors.</exception>
        public void StorePlaylist(IPlaylist playlist, bool removeFromChanged = true)
        {
            if (!PlaylistHandlers.TryGetValue(playlist.GetType(), out IPlaylistHandler playlistHandler))
                playlistHandler = DefaultHandler;
            string extension = playlistHandler.DefaultExtension;
            if (playlist.SuggestedExtension != null && playlistHandler.GetSupportedExtensions().Contains(playlist.SuggestedExtension))
                extension = playlist.SuggestedExtension;
            string fileName = playlist.Filename + "." + extension;
            playlistHandler.SerializeToFile(playlist, Path.Combine(PlaylistPath, fileName));
            if (removeFromChanged)
                RemoveFromChanged(playlist);
        }

        /// <summary>
        /// Attempts to create an <see cref="IPlaylist"/> from a file with the given <paramref name="fileName"/>.
        /// Returns null if there is no registered <see cref="IPlaylistHandler"/> for the given type.
        /// All other failure cases throw an Exception.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="PlaylistSerializationException"></exception>
        protected IPlaylist? LoadPlaylistFromFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException(nameof(fileName), "fileName cannot be null or empty.");
            IPlaylist? playlist = null;
            string[] files = Directory.GetFiles(PlaylistPath);
            string file = files.FirstOrDefault(f => fileName.Equals(Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase));
            string? fileExtension = null;
            if (file != null)
            {
                fileExtension = Path.GetExtension(file).TrimStart('.');
                if (fileExtension != null && PlaylistExtensionHandlers.TryGetValue(fileExtension, out IPlaylistHandler handler))
                {
                    playlist = handler.Deserialize(file);
                    playlist.SuggestedExtension = fileExtension;
                    if (playlist != null)
                    {
                        playlist.Filename = Path.GetFileNameWithoutExtension(file);
                        RegisterPlaylist(playlist, false);
                    }
                }
            }
            return playlist;
        }

        /// <summary>
        /// Registers an <see cref="IPlaylist"/> with the <see cref="PlaylistManager"/>. 
        /// </summary>
        /// <param name="playlist">Playlist to register.</param>
        /// <param name="asChanged">Immediately mark the playlist as changed.</param>
        /// <returns>True if <paramref name="playlist"/> was successful registered, 
        /// false if a playlist with the same filename is already registered.</returns>
        /// <exception cref="InvalidOperationException">Thrown if <paramref name="playlist"/> does not have a file name.</exception>
        public bool RegisterPlaylist(IPlaylist playlist, bool asChanged = true)
        {
            if (!string.IsNullOrEmpty(playlist.Filename))
            {
                if (LoadedPlaylists.TryAdd(playlist.Filename.ToUpper(), playlist))
                {
                    playlist.PlaylistChanged += OnPlaylistChanged;
                    if (asChanged)
                        MarkPlaylistChanged(playlist);
                    return true;
                }
                else
                    return false;
            }
            throw new InvalidOperationException("Playlist Filename cannot be null or empty.");
        }

        /// <summary>
        /// Attempts to get a loaded <see cref="IPlaylist"/> with the given filename.
        /// </summary>
        /// <param name="fileName">Filename without extension or directories</param>
        /// <param name="playlist">The retrieved playlist, null if there was no matching playlist.</param>
        /// <returns>True if a playlist was retrieved, false otherwise.</returns>
        public bool TryGetPlaylist(string fileName, out IPlaylist? playlist)
        {
            return LoadedPlaylists.TryGetValue(fileName.ToUpper(), out playlist);
        }

        /// <summary>
        /// Mark <paramref name="playlist"/> as changed in this <see cref="PlaylistManager"/>.
        /// </summary>
        /// <param name="playlist"></param>
        public void MarkPlaylistChanged(IPlaylist playlist)
        {
            lock (_changedLock)
            {
                ChangedPlaylists.Add(playlist);
            }
        }

        private void RemoveFromChanged(IPlaylist playlist)
        {
            lock (_changedLock)
            {
                ChangedPlaylists.Remove(playlist);
            }
        }
        /// <summary>
        /// Returns true if the given <see cref="IPlaylist"/> is marked as changed by this <see cref="PlaylistManager"/>.
        /// </summary>
        /// <param name="playlist"></param>
        /// <returns>True if the playlist is marked as changed, false otherwise.</returns>
        public bool IsPlaylistChanged(IPlaylist playlist)
        {
            if (playlist == null)
                return false;
            return ChangedPlaylists.Contains(playlist);
        }

        /// <summary>
        /// Retrieves the specified playlist. If the playlist doesn't exist, returns null.
        /// </summary>
        /// <param name="playlistFileName"></param>
        /// <returns></returns>
        public IPlaylist? GetPlaylist(string playlistFileName)
        {
            if (string.IsNullOrEmpty(playlistFileName))
                return null;
            IPlaylist? playlist = null;

            // Check if this playlist exists in CustomPlaylists
            if (playlist == null)
            {
                TryGetPlaylist(playlistFileName, out playlist);
            }

            // Try to load from file
            if (playlist == null)
            {
                playlist = LoadPlaylistFromFile(playlistFileName);
            }
            return playlist;
        }

        /// <summary>
        /// Attempts to get or load a playlist with the given filename. 
        /// If the playlist doesn't exist, it will be created by <paramref name="playlistFactory"/>.
        /// </summary>
        /// <param name="playlistFileName"></param>
        /// <param name="playlistFactory"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="PlaylistSerializationException"></exception>
        public IPlaylist GetOrAdd(string playlistFileName, Func<IPlaylist> playlistFactory)
        {
            if (string.IsNullOrEmpty(playlistFileName))
                throw new ArgumentNullException(nameof(playlistFileName), "playlistFileName cannot be null or empty.");
            IPlaylist? playlist = GetPlaylist(playlistFileName);

            if (playlist == null)
            {
                playlist = playlistFactory() ?? throw new ArgumentException("playlistFactory returned a null IPlaylist.", nameof(playlistFactory));
                playlist.Filename = playlistFileName;
                RegisterPlaylist(playlist);
            }

            return playlist;
        }
    }
}
