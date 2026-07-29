using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using LibmpvIptvClient.Architecture.Presentation.Mvvm;

namespace LibmpvIptvClient.Architecture.Presentation.Mvvm.MainWindow
{
    public class MainWindowDragDropActionsViewModel : ViewModelBase
    {
        private readonly MainShellViewModel _shell;

        public MainWindowDragDropActionsViewModel(MainShellViewModel shell)
        {
            _shell = shell;
        }

        public void OnDrop(System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    ProcessDroppedFiles(files);
                }
            }
        }

        public void ProcessDroppedFiles(string[] files)
        {
            // 优先处理第一个文件
            var file = files[0];
            var ext = Path.GetExtension(file).ToLowerInvariant();

            LibmpvIptvClient.Diagnostics.Logger.Info($"收到文件: {file}");

            if (ext == ".m3u" || ext == ".m3u8" || ext == ".txt")
            {
                // 认为是播放列表
                _shell.SourceLoader.UpdateLastSource(file, null);
                _ = _shell.LoadChannels(file);
            }
            else if (ext == ".json")
            {
                // 可能是 Sidecar 文件，或者是录播元数据
                // 暂时尝试作为普通流加载（如果是流地址 JSON，需另外处理，这里简化为交给 MPV 或 Parser）
                // 现阶段策略：JSON 可能含频道列表，尝试按 M3U 逻辑解析（M3UParser 可能支持 JSON？）
                // 如果不支持，则视为失败
                // 假设：用户拖入的是频道列表 JSON
                _shell.SourceLoader.UpdateLastSource(file, null);
                _ = _shell.LoadChannels(file);
            }
            else
            {
                // 其他视为媒体文件，直接播放
                _shell.LoadSingleStream(file);
            }

            // 如果支持多文件拖入（如批量添加 M3U），可在此扩展
            if (files.Length > 1)
            {
                LibmpvIptvClient.Diagnostics.Logger.Info("暂不支持批量拖入，仅处理了第一个文件。");
            }
        }
    }
}
