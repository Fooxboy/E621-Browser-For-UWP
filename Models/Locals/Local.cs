﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.UI.Popups;
using Newtonsoft.Json;
using E621Downloader.Models.Download;
using E621Downloader.Models.Locals;
using E621Downloader.Models.Posts;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.Storage.FileProperties;

namespace E621Downloader.Models.Locals {
	public static class Local {
		private static bool initialized = false;
		private const string FOLLOWLISTNAME = "FollowList.txt";
		private const string BLACKLISTNAME = "BlackList.txt";
		private const string TOKENNAME = "Token.txt";
		private const string DOWNLOADSINFONAME = "DownloadsInfo.json";
		private const string FAVORITESLISTNAME = "FavorutesList.json";
		private const string LOCALSETTINGSNAME = "LocalSettings.settings";

		private static StorageFolder LocalFolder => ApplicationData.Current.LocalFolder;

		public static StorageFile FollowListFile { get; private set; }
		public static StorageFile BlackListFile { get; private set; }

		public static StorageFile FutureAccessTokenFile { get; private set; }
		public static StorageFile DownloadsInfoFile { get; private set; }
		public static StorageFile FavoritesListFile { get; private set; }
		public static StorageFile LocalSettingsFile { get; private set; }

		public static string[] FollowList { get; private set; }
		public static string[] BlackList { get; private set; }

		private static string token;

		public static StorageFolder DownloadFolder { get; private set; }

		public async static Task Initialize() {
			Debug.WriteLine("Initializing Local");
			Debug.WriteLine(LocalFolder.Path);
			if(initialized) {
				throw new Exception("Local has been initialized more than one time!");
			}
			initialized = true;
			FollowListFile = await LocalFolder.CreateFileAsync(FOLLOWLISTNAME, CreationCollisionOption.OpenIfExists);
			BlackListFile = await LocalFolder.CreateFileAsync(BLACKLISTNAME, CreationCollisionOption.OpenIfExists);

			FutureAccessTokenFile = await LocalFolder.CreateFileAsync(TOKENNAME, CreationCollisionOption.OpenIfExists);

			DownloadsInfoFile = await LocalFolder.CreateFileAsync(DOWNLOADSINFONAME, CreationCollisionOption.OpenIfExists);

			FavoritesListFile = await LocalFolder.CreateFileAsync(FAVORITESLISTNAME, CreationCollisionOption.OpenIfExists);

			LocalSettingsFile = await LocalFolder.CreateFileAsync(LOCALSETTINGSNAME, CreationCollisionOption.OpenIfExists);

			await Reload();
		}

		//public async static Task<List<DownloadInstanceLocalManager.DownloadInstanceLocal>> GetDownloadsInfo() {
		//	Stream stream = await DownloadsInfoFile.OpenStreamForReadAsync();
		//	StreamReader reader = new StreamReader(stream);
		//	var ReList = JsonConvert.DeserializeObject<List<DownloadInstanceLocalManager.DownloadInstanceLocal>>(reader.ReadToEnd());
		//	return ReList;
		//}

		//public async static void WriteDownloadsInfo() {
		//	string json = JsonConvert.SerializeObject(DownloadsManager.downloads);
		//	await FileIO.WriteTextAsync(DownloadsInfoFile, json);
		//}


		public async static Task WriteTokenToFile(string token) {
			await FileIO.WriteTextAsync(FutureAccessTokenFile, token);
			await SetToken(token);
		}

		public static string GetToken() => token;
		public async static Task SetToken(string token) {
			Local.token = token;
			if(string.IsNullOrEmpty(token)) {
				//set to download library
				return;
			}
			try {
				DownloadFolder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
			} catch(ArgumentException e) {
				Debug.WriteLine(e);
			}
		}

		public async static void ClearToken(string token) {
			StorageApplicationPermissions.FutureAccessList.Remove(token);
			Local.token = null;
			DownloadFolder = null;
			await FileIO.WriteTextAsync(FutureAccessTokenFile, "");
		}

		private async static Task<string> GetTokenFromFile() {
			Stream stream = await FutureAccessTokenFile.OpenStreamForReadAsync();
			StreamReader reader = new StreamReader(stream);
			return await reader.ReadToEndAsync();
		}


		public async static Task Reload() {
			FollowList = await GetFollowList();
			BlackList = await GetBlackList();
			await ReadLocalSettings();
			await SetToken(await GetTokenFromFile());
			await ReadFavoritesLists();
		}

		public static void AddFollowList(string newTag) {
			var list = FollowList.ToList();
			list.Add(newTag);
			FollowList = list.ToArray();
			WriteFollowList(FollowList);
		}

		public static void AddBlackList(string newTag) {
			var list = BlackList.ToList();
			list.Add(newTag);
			BlackList = list.ToArray();
			WriteBlackList(BlackList);
		}

		public static void RemoveFollowList(string tag) {
			var list = FollowList.ToList();
			list.Remove(tag);
			FollowList = list.ToArray();
			WriteFollowList(FollowList);
		}

		public static void RemoveBlackList(string tag) {
			var list = BlackList.ToList();
			list.Remove(tag);
			BlackList = list.ToArray();
			WriteBlackList(BlackList);
		}

		public static bool CheckFollowList(string tag) => FollowList.Contains(tag);
		public static bool CheckFollowList(string[] tags) => FollowList.Contains(E621Tag.JoinTags(tags));
		public static bool CheckBlackList(string tag) => BlackList.Contains(tag);
		public static bool CheckBlackList(string[] tags) => BlackList.Contains(E621Tag.JoinTags(tags));

		public async static void WriteFollowList(string[] list) {
			await FileIO.WriteLinesAsync(FollowListFile, list);
			FollowList = list;
		}
		public async static void WriteBlackList(string[] list) {
			await FileIO.WriteLinesAsync(BlackListFile, list);
			BlackList = list;
		}

		private async static Task<string[]> GetFollowList() => await GetListFromFile(FollowListFile);
		private async static Task<string[]> GetBlackList() => await GetListFromFile(BlackListFile);
		private async static Task<string[]> GetListFromFile(StorageFile file) {
			var list = new List<string>();
			using(Stream stream = await file.OpenStreamForReadAsync()) {
				using(StreamReader reader = new StreamReader(stream)) {
					string line = "";
					foreach(char c in await reader.ReadToEndAsync()) {
						if(c == '\r' || c == '\n') {
							if(line.Length > 0) {
								list.Add(line);
							}
							line = "";
							continue;
						}
						line += c;
					}
					if(line.Length > 0) {
						list.Add(line);
					}
				}
			}
			return list.ToArray();
		}

		public static MetaFile CreateMetaFile(StorageFile file, Post post, string groupName) {
			MetaFile meta = new MetaFile(file.Path, groupName, post);
			WriteMetaFile(meta, file, post);
			return meta;
		}
		private async static void WriteMetaFile(MetaFile meta, StorageFile file, Post post) {
			StorageFolder folder = await file.GetParentAsync();
			StorageFile target = await folder.CreateFileAsync($"{post.id}.meta", CreationCollisionOption.ReplaceExisting);
			try {
				await FileIO.WriteTextAsync(target, meta.ConvertJson());
			} catch(Exception e) {
				Debug.WriteLine(e.Message);
			}
		}
		public async static void WriteMetaFile(MetaFile meta, Post post, string groupName) {
			(MetaFile, StorageFile) file = await GetMetaFile(post.id.ToString(), groupName);
			WriteMetaFile(meta, file.Item2, post);
		}

		public async static Task<StorageFolder[]> GetDownloadsFolders() {
			return DownloadFolder == null ? null : (await DownloadFolder.GetFoldersAsync()).ToArray();
		}
		private class Pair {
			public MetaFile meta;
			public BitmapImage source;
			public StorageFile file;
			public string SourceID => file?.DisplayName;

			public bool IsValid => meta != null /*&& source != null */&& SourceID != null;

			public static void Add(List<Pair> list, MetaFile meta) {
				foreach(var item in list) {
					if(item.file == null) {//not sure
						continue;
					}
					if(item.SourceID == meta.MyPost.id.ToString()) {
						item.meta = meta;
						return;
					}
				}
				list.Add(new Pair() { meta = meta });
			}
			public static void Add(List<Pair> list, BitmapImage source, StorageFile file) {
				foreach(var item in list) {
					if(item.meta != null && item.meta.MyPost.id.ToString() == file.DisplayName) {
						item.file = file;
						item.source = source;
						return;
					}
				}
				list.Add(new Pair() { file = file, source = source });
			}

			public static (MetaFile, BitmapImage, StorageFile) Convert(Pair pair) {
				return (pair.meta, pair.source, pair.file);
			}

			public static List<(MetaFile, BitmapImage, StorageFile)> Convert(List<Pair> list, Func<Pair, bool> check) {
				var result = new List<(MetaFile, BitmapImage, StorageFile)>();
				foreach(Pair item in list) {
					if((check?.Invoke(item)).Value) {
						result.Add((item.meta, item.source, item.file));
					}
				}
				return result;
			}
		}

		public async static Task<List<(MetaFile, BitmapImage, StorageFile)>> GetMetaFiles(string folderName) {
			var result = new List<(MetaFile, BitmapImage)>();
			StorageFolder folder = await DownloadFolder.GetFolderAsync(folderName);
			var pairs = new List<Pair>();
			foreach(StorageFile file in await folder.GetFilesAsync()) {
				if(file.FileType == ".meta") {
					MetaFile meta;
					using(Stream stream = await file.OpenStreamForReadAsync()) {
						using(StreamReader reader = new StreamReader(stream)) {
							meta = JsonConvert.DeserializeObject<MetaFile>(await reader.ReadToEndAsync());
						}
					}
					if(meta != null) {
						Pair.Add(pairs, meta);
					}
				} else {
					BitmapImage bitmap = new BitmapImage();
					ThumbnailMode mode = ThumbnailMode.SingleItem;
					if(new string[] { ".webm" }.Contains(file.FileType)) {
						mode = ThumbnailMode.SingleItem;
					} else if(new string[] { ".jpg", ".png" }.Contains(file.FileType)) {
						mode = ThumbnailMode.PicturesView;
					}
					//Debug.WriteLine(mode);
					using(StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(mode)) {
						if(thumbnail != null) {
							using(Stream stream = thumbnail.AsStreamForRead()) {
								await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
							}
						}
					}
					Pair.Add(pairs, bitmap, file);
				}
			}
			return Pair.Convert(pairs, p => p.IsValid);
		}

		public async static Task<(MetaFile, StorageFile)> GetMetaFile(string postID, string groupName) {
			StorageFolder folder = await DownloadFolder.GetFolderAsync(groupName);
			StorageFile file = await folder.GetFileAsync($"{postID}.meta");
			using(Stream stream = await file.OpenStreamForReadAsync()) {
				using(StreamReader reader = new StreamReader(stream)) {
					return (JsonConvert.DeserializeObject<MetaFile>(await reader.ReadToEndAsync()), file);
				}
			}
		}

		public async static Task<List<MetaFile>> GetAllMetaFiles() {
			var metas = new List<MetaFile>();
			foreach(StorageFolder folder in await DownloadFolder.GetFoldersAsync()) {
				foreach(StorageFile file in await folder.GetFilesAsync()) {
					if(file.FileType != ".meta") {
						continue;
					}
					using(Stream stream = await file.OpenStreamForReadAsync()) {
						using(StreamReader reader = new StreamReader(stream)) {
							string content = await reader.ReadToEndAsync();
							MetaFile meta = JsonConvert.DeserializeObject<MetaFile>(content);
							if(meta == null) {
								continue;
							}
							metas.Add(meta);
						}
					}
				}
			};
			return metas;
		}


		public async static void UpdateMetaFile(StorageFile file, MetaFile meta) {
			await FileIO.WriteTextAsync(file, meta.ConvertJson());
		}

		public async static Task<List<MetaFile>> FindAllMetaFiles() {
			var result = new List<MetaFile>();

			foreach(StorageFolder folder in await DownloadFolder.GetFoldersAsync()) {
				foreach(StorageFile item in await folder.GetFilesAsync()) {
					using(Stream stream = await item.OpenStreamForReadAsync()) {
						using(StreamReader reader = new StreamReader(stream)) {
							result.Add(JsonConvert.DeserializeObject(await reader.ReadToEndAsync()) as MetaFile);
						}
					}
				}
			}
			return result;
		}

		public async static void WriteLocalSettings() {
			await FileIO.WriteTextAsync(LocalSettingsFile, JsonConvert.SerializeObject(LocalSettings.Current));
		}

		public async static Task ReadLocalSettings() {
			using(Stream stream = await LocalSettingsFile.OpenStreamForReadAsync()) {
				using(StreamReader reader = new StreamReader(stream)) {
					LocalSettings.Current = JsonConvert.DeserializeObject<LocalSettings>(await reader.ReadToEndAsync());
				}
			}
			if(LocalSettings.Current == null) {
				LocalSettings.Current = new LocalSettings() {
					customHostEnable = false,
					spot_allowGif = true,
					spot_allowWebm = true,
					spot_allowImage = true,
					spot_includeExplicit = true,
					spot_includeQuestoinable = false,
					spot_includeSafe = false,
					cycleList = true,
					concatTags = false,
					mediaBackgroundPlay = false,
					mediaAutoPlay = true,
					customHost = "",
					spot_amount = 1,
				};
				WriteLocalSettings();
			}
		}

		public async static Task ReadFavoritesLists() {
			using(Stream stream = await FavoritesListFile.OpenStreamForReadAsync()) {
				using(StreamReader reader = new StreamReader(stream)) {
					FavoritesList.Table = JsonConvert.DeserializeObject<List<FavoritesList>>(await reader.ReadToEndAsync()) ?? new List<FavoritesList>();
				}
			}
		}

		public async static Task WriteFavoritesLists() {
			await FileIO.WriteTextAsync(FavoritesListFile, JsonConvert.SerializeObject(FavoritesList.Table));
		}

		//F:\E621\creepypasta -momo_(creepypasta) rating;e\1820721.png
		public async static Task<(StorageFile, MetaFile)> GetDownloadFile(string path) {
			StorageFile file;
			try {
				file = await StorageFile.GetFileFromPathAsync(path);
			} catch(FileNotFoundException) {
				return (null, null);
			}
			string metaPath = path.Substring(0, path.LastIndexOf('.')) + ".meta";
			StorageFile metaFile;
			try {
				metaFile = await StorageFile.GetFileFromPathAsync(metaPath);
			} catch(FileNotFoundException) {
				return (file, null);
			}
			MetaFile meta;
			using(Stream stream = await metaFile.OpenStreamForReadAsync()) {
				using(StreamReader reader = new StreamReader(stream)) {
					meta = JsonConvert.DeserializeObject<MetaFile>(await reader.ReadToEndAsync());
				}
			}
			return (file, meta);
		}
	}
}
