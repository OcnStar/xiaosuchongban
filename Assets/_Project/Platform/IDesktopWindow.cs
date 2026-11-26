using System;

namespace Xiaosu.Platform
{
    public interface IDesktopWindow : IDisposable
    {
        void SetAlwaysOnTop(bool enabled);
        void SetClickThrough(bool enabled);
    }
}
