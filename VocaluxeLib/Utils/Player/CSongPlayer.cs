﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

using VocaluxeLib.Draw;
using VocaluxeLib.Songs;

namespace VocaluxeLib.Utils.Player
{
    public class CSongPlayer : CSoundPlayer
    {
        private CSong _Song;
        private bool _VideoEnabled;
        private int _Video = -1;
        private CTexture _CurrentVideoTexture;
        private CFading _VideoFading;

        public bool VideoEnabled
        {
            get { return _VideoEnabled; }
            set
            {
                if (_VideoEnabled == value)
                    return;
                _VideoEnabled = value;
                if (_VideoEnabled)
                    _LoadVideo();
                else if (_Video != -1)
                {
                    CBase.Video.Close(_Video);
                    CBase.Drawing.RemoveTexture(ref _CurrentVideoTexture);
                    _Video = -1;
                }
            }
        }
        public bool CanSing { get; private set; }
        public int SongID
        {
            get { return _Song == null ? -1 : _Song.ID; }
        }
        public bool SongHasVideo
        {
            get { return _Song != null && File.Exists(Path.Combine(_Song.Folder,_Song.VideoFileName)); }
        }

        public string ArtistAndTitle
        {
            get
            {
                if (_Song == null)
                    return "";
                if (_Song.Artist != "" && _Song.Title != "")
                    return _Song.Artist + " - " + _Song.Title;
                return Path.GetFileNameWithoutExtension(_Song.MP3FileName);
            }
        }

        public CTexture Cover
        {
            get { return _Song == null ? CBase.Cover.GetNoCover() : _Song.CoverTextureBig; }
        }

        public bool SongLoaded
        {
            get { return _StreamID != -1; }
        }

        public float Position
        {
            get
            {
                if (_StreamID == -1)
                    return -1;
                return CBase.Sound.GetPosition(_StreamID);
            }
            set
            {
                if (_StreamID == -1)
                    return;
                CBase.Sound.SetPosition(_StreamID, value);
                CBase.Video.Skip(_Video, value, _Song.VideoGap);
                _StartPosition = value;
            }
        }

        public CSongPlayer(bool loop = false) : base(loop) { }

        public CSongPlayer(CSong song, bool loop = false, float position = 0f, bool autoplay = false)
            : base(Path.Combine(song.Folder,song.MP3FileName), loop, position, autoplay)
        {
            _Song = song;
            _LoadVideo();
        }

        public CSongPlayer(string file, bool loop = false, float position = 0f, bool autoplay = false)
            : base(file, loop, position, autoplay)
        {
            _Song = null;
        }

        public CTexture GetVideoTexture()
        {
            if (_Video == -1 || _Song == null)
                return null;
            float vtime;
            if (CBase.Video.GetFrame(_Video, ref _CurrentVideoTexture, CBase.Sound.GetPosition(_StreamID), out vtime))
            {
                if (_VideoFading != null)
                {
                    bool finished;
                    _CurrentVideoTexture.Color.A = _VideoFading.GetValue(out finished);
                    if (finished)
                        _VideoFading = null;
                }
                return _CurrentVideoTexture;
            }
            return null;
        }

        public void Load(CSong song, float position = 0f, bool autoplay = false) 
        {
            if (song == null)
                return;

            Close();

            _Song = song;
            _LoadVideo();
            base.Load(Path.Combine(song.Folder, song.MP3FileName), position, autoplay);
        }

        public void LoadFile(string file, float position = 0f, bool autoplay = false)
        {
            if (String.IsNullOrEmpty(file))
                return;

            Close();

            _Song = null;
            base.Load(file, position, autoplay);
        }

        public new void Close()
        {
            base.Close();

            if (_Video != -1)
            {
                CBase.Video.Close(_Video);
                CBase.Drawing.RemoveTexture(ref _CurrentVideoTexture);
                _Video = -1;
            }
        }

        public new void Play()
        {
            base.Play();

            if (_VideoEnabled && _Video != -1)
                CBase.Video.Resume(_Video);
        }

        public new void Stop()
        {
            base.Stop();

            if (_Video != -1)
            {
                CBase.Video.Close(_Video);
                CBase.Drawing.RemoveTexture(ref _CurrentVideoTexture);
                _Video = -1;
            }
        }

        public new void TogglePause()
        {
            base.TogglePause();
            if (!IsPlaying && _Video != -1)
            {
                CBase.Video.Pause(_Video);
                CBase.Video.Skip(_Video, CBase.Sound.GetPosition(_StreamID) + _FadeTime, _Song.VideoGap);
            }
            else if(_Video != -1)
            {
                CBase.Video.Resume(_Video);
            }
        }

        private void _LoadVideo()
        {
            if (_Song == null)
                return;

            string videoFilePath = Path.Combine(_Song.Folder, _Song.VideoFileName);
            if (_Video != -1 || String.IsNullOrEmpty(videoFilePath))
                return;
            _Video = CBase.Video.Load(videoFilePath);
            CBase.Video.Skip(_Video, 0f, _Song.VideoGap + _StartPosition); //Use gap, otherwhise we will overwrite position in GetVideoTexture
            _VideoFading = new CFading(0f, 1f, 3f);
        }
    }
}
