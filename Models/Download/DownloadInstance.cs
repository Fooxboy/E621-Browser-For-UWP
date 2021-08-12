﻿using E621Downloader.Models.Locals;
using E621Downloader.Models.Posts;
using E621Downloader.Pages.DownloadSection;
using E621Downloader.Pages.LibrarySection;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Networking.BackgroundTransfer;

namespace E621Downloader.Models.Download {
	public class DownloadInstance {
		public Post PostRef { get; private set; }
		public string GroupName { get; set; }

		public MetaFile metaFile;

		public DownloadOperation Operation { get; private set; }

		public double DownloadProgress { get; private set; }

		public BackgroundTransferStatus Status => Operation.Progress.Status;

		public int Percentage => (int)Math.Ceiling(DownloadProgress * 100);

		public ulong BytesReceived => Operation.Progress.BytesReceived;
		public ulong TotalBytesToReceive => Operation.Progress.TotalBytesToReceive;

		public string ReceivedKB => (BytesReceived / 1000).ToString();
		public string TotalKB => (TotalBytesToReceive / 1000).ToString();

		public Action<double> DownloadingAction { get; set; }
		public Action DownloadCompleteAction { get; set; }

		public DownloadInstance(Post post, string groupName, DownloadOperation operation) {
			PostRef = post;
			GroupName = groupName;
			Operation = operation;
		}

		public async void StartDownload() {
			await Operation.StartAsync().AsTask(new CancellationTokenSource().Token, new Progress<DownloadOperation>(o => {
				ulong received = Operation.Progress.BytesReceived;
				ulong total = Operation.Progress.TotalBytesToReceive;
				if(total == 0) {
					DownloadProgress = -1;
				} else {
					DownloadProgress = received / (double)total;
				}
				DownloadingAction?.Invoke(DownloadProgress);
				if(DownloadProgress == 1 || Status == BackgroundTransferStatus.Completed) {
					DownloadCompleteAction?.Invoke();
					metaFile.FinishedDownloading = true;
					Local.WriteMetaFile(metaFile, PostRef, GroupName);
					if(LibraryPage.Instance != null && LibraryPage.Instance.current != null) {
						LibraryPage.Instance.current.RefreshRequest();
					}
				}
			}));
			//if(MainPage.Instance.currentTag == PageTag.Download && DownloadPage.Instance != null) {
			//	DownloadPage.Instance.RefreshCurrentContent();
			//}
		}

		public void Pause() {
			Operation.Pause();
		}

		public void Resume() {
			if(Status == BackgroundTransferStatus.Running) {
				return;
			}
			try {
				Operation.Resume();
			} catch(InvalidOperationException ex) {
				Debug.WriteLine(ex);
			}
		}

		public void Cancel() {
			//Operation.
		}
	}
}
