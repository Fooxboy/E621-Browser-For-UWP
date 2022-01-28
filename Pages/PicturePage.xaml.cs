﻿using E621Downloader.Models;
using E621Downloader.Models.Download;
using E621Downloader.Models.Locals;
using E621Downloader.Models.Networks;
using E621Downloader.Models.Posts;
using E621Downloader.Pages.LibrarySection;
using E621Downloader.Views;
using E621Downloader.Views.CommentsSection;
using Microsoft.Toolkit.Uwp.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Networking.BackgroundTransfer;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace E621Downloader.Pages {
	public sealed partial class PicturePage: Page {
		public Post PostRef { get; private set; }
		public PathType PostType { get; private set; }
		public readonly ObservableCollection<GroupTagListWithColor> tags;
		private readonly Dictionary<string, E621Tag> tags_pool;//should i refresh on every entry?
		public readonly List<E621Comment> comments;

		private bool commentsLoading;
		private bool commentsLoaded;

		private string path;

		public bool EnableAutoPlay => LocalSettings.Current?.mediaAutoPlay ?? false;
		public string Title => PostRef == null ? "# No Post" :
			$"#{PostRef.id} ({PostRef.rating.ToUpper()})";

		private CancellationTokenSource cts;

		private DataPackage imageDataPackage;
		private bool TextInputing { get; set; } = false;

		public PicturePage() {
			this.InitializeComponent();
			this.NavigationCacheMode = NavigationCacheMode.Enabled;
			tags = new ObservableCollection<GroupTagListWithColor>();
			tags_pool = new Dictionary<string, E621Tag>();
			comments = new List<E621Comment>();
			this.DataContextChanged += (s, c) => Bindings.Update();
			MyMediaPlayer.MediaPlayer.IsLoopingEnabled = true;
			KeyListener.SubmitInstance(new KeyListenerInstance(key => {
				if(MainPage.Instance.currentTag != PageTag.Picture) {
					return;
				}
				if(TextInputing) {
					return;
				}
				if(this.PostRef == null) {
					return;
				}
				if(key == VirtualKey.A || key == VirtualKey.Left) {
					GoLeft();
				} else if(key == VirtualKey.D || key == VirtualKey.Right) {
					GoRight();
				} else if(key == VirtualKey.W || key == VirtualKey.Up) {
					ZoomIn();
				} else if(key == VirtualKey.S || key == VirtualKey.Down) {
					ZoomOut();
				}
			}));
		}

		protected async override void OnNavigatedTo(NavigationEventArgs e) {
			base.OnNavigatedTo(e);
			object p = e.Parameter;
			MainPage.ClearPicturePageParameter();
			MyMediaPlayer.AutoPlay = EnableAutoPlay;
			bool showNoPostGrid = false;
			this.imageDataPackage = null;
			if(p == null && PostRef == null && PostsBrowser.Instance != null && PostsBrowser.Instance.Posts != null && PostsBrowser.Instance.Posts.Count > 0) {
				App.postsList.UpdatePostsList(PostsBrowser.Instance.Posts);
				p = PostsBrowser.Instance.Posts[0];
				App.postsList.Current = p;
			}
			if(p is Post post) {
				if(PostRef == post) {
					NoPostGrid.Visibility = Visibility.Collapsed;//just in case
					MainGrid.Visibility = Visibility.Visible;
					return;
				}
				PostRef = post;
				UpdateDownloadButton(false);
				await LoadFromPost(post);
				PostType = PathType.PostID;
				path = this.PostRef.id;
			} else if(p is MixPost mix) {
				switch(mix.Type) {
					case PathType.PostID:
						if(cts != null) {
							try {
								cts.Cancel();
								cts.Dispose();
							} finally {
								cts = null;
							}
						}
						if(mix.PostRef == this.PostRef) {
							return;
						}
						if(mix.PostRef != null) {
							this.PostRef = mix.PostRef;
						} else {
							cts = new CancellationTokenSource();
							this.PostRef = await Post.GetPostByIDAsync(cts.Token, mix.ID);
							mix.PostRef = this.PostRef;
						}
						UpdateDownloadButton(false);
						await LoadFromPost(this.PostRef);
						PostType = PathType.PostID;
						path = mix.ID;
						break;
					case PathType.Local:
						if(!mix.LocalLoaded) {
							(StorageFile, MetaFile) result = await Local.GetDownloadFile(path);
							mix.ImageFile = result.Item1;
							mix.MetaFile = result.Item2;
						}
						this.PostRef = mix.MetaFile.MyPost;
						UpdateDownloadButton(true);
						await LoadFromLocal(mix);
						PostType = PathType.Local;
						path = mix.LocalPath;
						break;
					default:
						throw new PathTypeException();
				}
			} else if(p is ILocalImage local) {
				if(PostRef == local.ImagePost) {
					return;
				}
				PostRef = local.ImagePost;
				UpdateDownloadButton(true);
				await LoadFromLocal(local);
				PostType = PathType.Local;
				path = local.ImageFile.Path;
			} else if(this.PostRef == null && p == null) {
				MyProgressRing.IsActive = false;
				showNoPostGrid = true;
				PostType = PathType.PostID;
			}
			TagsListView.ScrollIntoView(TagsListView.Items.FirstOrDefault());
			UpdateTagsGroup(PostRef?.tags);
			NoPostGrid.Visibility = showNoPostGrid ? Visibility.Visible : Visibility.Collapsed;
			MainGrid.Visibility = !showNoPostGrid ? Visibility.Visible : Visibility.Collapsed;
			TitleText.Text = Title;
			UpdateScore();
			UpdateRatingColor();
			UpdateTypeIcon();
			UpdateSoundIcon();
			UpdateFavoriteButton();
			UpdateDescriptionSection();
			UpdateOthers();
			ResetImage();
			if(this.PostRef != null && p != null) {
				MainSplitView.IsPaneOpen = false;
				//InformationPivot.SelectedIndex = 0;
				commentsLoaded = false;
				commentsLoading = false;
				comments.Clear();
				CommentsListView.Items.Clear();
				if(InformationPivot.SelectedIndex == 1) {
					LoadCommentsAsync();
				}

				ChildrenGridView.Items.Clear();
				foreach(string item in this.PostRef.relationships.children) {
					var i = new ImageHolderForPicturePage() {
						Post_ID = item,
						Height = 220,
						Width = 235,
						Origin = this.PostRef,
					};
					ChildrenGridView.Items.Add(i);
				}
				ParentImageHolder.Post_ID = this.PostRef.relationships.parent_id;
				ParentImageHolder.Origin = this.PostRef;
			}

			if(hasExecutedPause && MyMediaPlayer.Source != null && MyMediaPlayer.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Paused) {
				MyMediaPlayer.MediaPlayer.Play();
				hasExecutedPause = false;
			}
		}

		private bool hasExecutedPause = false;
		protected override void OnNavigatedFrom(NavigationEventArgs e) {
			base.OnNavigatedFrom(e);
			if(!LocalSettings.Current.mediaBackgroundPlay && MyMediaPlayer.Source != null && MyMediaPlayer.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) {
				MyMediaPlayer.MediaPlayer.Pause();
				hasExecutedPause = true;
			}
		}

		private async Task LoadFromPost(Post post) {
			if(string.IsNullOrWhiteSpace(post.file.url)) {
				MyMediaPlayer.Visibility = Visibility.Collapsed;
				MyScrollViewer.Visibility = Visibility.Collapsed;
				MyProgressRing.IsActive = false;
				await MainPage.CreatePopupDialog("Error", $"Post({post.id}) has no valid file url");
				return;
			}
			FileType type = GetFileType(post);
			switch(type) {
				case FileType.Png:
				case FileType.Jpg:
				case FileType.Gif:
					MyProgressRing.IsActive = true;
					MyMediaPlayer.Visibility = Visibility.Collapsed;
					MyScrollViewer.Visibility = Visibility.Visible;
					MainImage.Source = new BitmapImage(new Uri(post.file.url));
					imageDataPackage = new DataPackage() {
						RequestedOperation = DataPackageOperation.Copy,
					};
					imageDataPackage.SetBitmap(RandomAccessStreamReference.CreateFromUri(new Uri(post.file.url)));
					MyMediaPlayer.MediaPlayer.Source = null;
					break;
				case FileType.Webm:
					MyProgressRing.IsActive = false;
					MyMediaPlayer.Visibility = Visibility.Visible;
					MyScrollViewer.Visibility = Visibility.Collapsed;
					if(!string.IsNullOrEmpty(post.file.url)) {
						MyMediaPlayer.Source = MediaSource.CreateFromUri(new Uri(post.file.url));
					}
					break;
				case FileType.Anim:
					MyMediaPlayer.Visibility = Visibility.Collapsed;
					MyScrollViewer.Visibility = Visibility.Collapsed;
					MyProgressRing.IsActive = false;
					break;
				default:
					await MainPage.CreatePopupDialog("Error", $"Type({type}) not supported");
					break;
			}
		}

		private async Task LoadFromLocal(ILocalImage local) {
			FileType type = GetFileType(local.ImagePost);
			HintText.Visibility = Visibility.Collapsed;
			switch(type) {
				case FileType.Png:
				case FileType.Jpg:
				case FileType.Gif:
					MyProgressRing.IsActive = true;
					MyMediaPlayer.Visibility = Visibility.Collapsed;
					MyScrollViewer.Visibility = Visibility.Visible;
					try {
						using(IRandomAccessStream randomAccessStream = await local.ImageFile.OpenAsync(FileAccessMode.Read)) {
							BitmapImage result = new BitmapImage();
							await result.SetSourceAsync(randomAccessStream);
							MainImage.Source = result;
							imageDataPackage = new DataPackage() {
								RequestedOperation = DataPackageOperation.Copy,
							};
							imageDataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(local.ImageFile));
						}
					} catch(Exception e) {
						await MainPage.CreatePopupDialog("Error", $"Local Post({local.ImagePost.id}) - {local.ImageFile.Path} Load Failed\n{e.Message}");
					}
					MyMediaPlayer.Source = null;
					MyProgressRing.IsActive = false;
					break;
				case FileType.Webm:
					MyProgressRing.IsActive = false;
					MyMediaPlayer.Visibility = Visibility.Visible;
					MyScrollViewer.Visibility = Visibility.Collapsed;
					MyMediaPlayer.Source = MediaSource.CreateFromStorageFile(local.ImageFile);
					MainImage.Source = null;
					break;
				case FileType.Anim:
					MyMediaPlayer.Visibility = Visibility.Collapsed;
					MyScrollViewer.Visibility = Visibility.Collapsed;
					HintText.Text = $"Type SWF not supported";
					HintText.Visibility = Visibility.Visible;
					MyProgressRing.IsActive = false;
					break;
				default:
					HintText.Text = $"Type ({type}) not supported";
					HintText.Visibility = Visibility.Visible;
					await MainPage.CreatePopupDialog("Error", $"Type({type}) not supported");
					break;
			}
		}

		private FileType GetFileType(Post post) {
			switch(post.file.ext.ToLower().Trim()) {
				case "jpg":
					return FileType.Jpg;
				case "png":
					return FileType.Png;
				case "gif":
					return FileType.Gif;
				case "anim":
				case "swf":
					return FileType.Anim;
				case "webm":
					return FileType.Webm;
				default:
					throw new Exception($"New Type({post.file.ext}) Found");
			}
		}

		private void UpdateDownloadButton(bool isLocal) {
			if(isLocal) {
				DownloadText.Text = "Local";
				DownloadIcon.Glyph = "\uE159";
				DownloadButton.IsEnabled = false;
			} else {
				DownloadText.Text = "Download";
				DownloadIcon.Glyph = "\uE118";
				DownloadButton.IsEnabled = true;
			}
		}

		private void UpdateScore() {
			if(PostRef == null) {
				return;
			}
			ScoreText.Text = $"{PostRef.score.total}";
			ToolTipService.SetToolTip(ScoreText, $"Upvote: {PostRef.score.up}\nDownvote: {Math.Abs(PostRef.score.down)}");
		}

		private void UpdateTypeIcon() {
			if(PostRef == null) {
				return;
			}
			switch(PostRef.file.ext.ToLower().Trim()) {
				case "jpg":
				case "png":
					TypeIcon.Glyph = "\uEB9F";
					break;
				case "gif":
					TypeIcon.Glyph = "\uF4A9";
					break;
				case "webm":
					TypeIcon.Glyph = "\uE714";
					break;
				default:
					TypeIcon.Glyph = "\uE9CE";
					break;
			}
			ToolTipService.SetToolTip(TypeIcon, $"Type: {PostRef.file.ext.Trim().ToCamelCase()}");
		}

		private void UpdateSoundIcon() {
			if(PostRef == null) {
				return;
			}
			var list = PostRef.tags.GetAllTags();
			if(list.Contains("sound_warning")) {
				SoundIcon.Visibility = Visibility.Visible;
				SoundIcon.Foreground = new SolidColorBrush(Colors.Red);
				ToolTipService.SetToolTip(SoundIcon, "This Video Has Sound_Warning Tag");
			} else if(list.Contains("sound")) {
				SoundIcon.Visibility = Visibility.Visible;
				SoundIcon.Foreground = new SolidColorBrush(Colors.Yellow);
				ToolTipService.SetToolTip(SoundIcon, "This Video Has Sound Tag");
			} else {
				SoundIcon.Visibility = Visibility.Collapsed;
			}
		}

		private void UpdateRatingColor() {
			if(PostRef == null) {
				return;
			}
			string rating = PostRef.rating.ToLower().Trim();
			Color color;
			string tooltip;
			if(rating == "e") {
				color = Colors.Red;
				tooltip = "Rating: Explicit";
			} else if(rating == "q") {
				color = Colors.Yellow;
				tooltip = "Rating: Questionable";
			} else if(rating == "s") {
				color = Colors.Green;
				tooltip = "Rating: Safe";
			} else {
				color = Colors.White;
				tooltip = "No Rating";
			}
			TitleText.Foreground = new SolidColorBrush(color);
			ToolTipService.SetToolTip(TitleText, tooltip);
			ToolTipService.SetPlacement(TitleText, PlacementMode.Bottom);
		}

		private void UpdateTagsGroup(Tags tags) {
			if(tags == null) {
				return;
			}
			RemoveGroup();
			AddNewGroup("Artist", tags.artist.ToGroupTag("#f2ac08".ToColor()));
			AddNewGroup("Copyright", tags.copyright.ToGroupTag("#d0d".ToColor()));
			AddNewGroup("Species", tags.species.ToGroupTag("#ed5d1f".ToColor()));
			AddNewGroup("Character", tags.character.ToGroupTag("#0a0".ToColor()));
			AddNewGroup("General", tags.general.ToGroupTag("#b4c7d9".ToColor()));
			AddNewGroup("Meta", tags.meta.ToGroupTag("#fff".ToColor()));
			AddNewGroup("Invalid", tags.invalid.ToGroupTag("#ff3d3d".ToColor()));
			AddNewGroup("Lore", tags.lore.ToGroupTag("#282".ToColor()));

		}

		private void RemoveGroup() {
			tags.Clear();
		}

		private void AddNewGroup(string title, List<GroupTag> content) {
			if(content == null) {
				return;
			}
			if(content.Count == 0) {
				return;
			}
			tags.Add(new GroupTagListWithColor(title, content));
		}

		private async void LoadCommentsAsync() {
			comments.Clear();
			CommentsListView.Items.Clear();
			commentsLoaded = false;
			commentsLoading = true;
			LoadingSection.Visibility = Visibility.Visible;
			CommentsHint.Visibility = Visibility.Collapsed;
			E621Comment[] list = await E621Comment.GetAsync(PostRef.id);
			CancelLoadAvatars();
			if(list != null && list.Length > 0) {
				foreach(E621Comment item in list.Reverse()) {
					comments.Add(item);
					CommentsListView.Items.Add(new CommentView(item));
				}
				LoadAllAvatars();
			} else {
				CommentsHint.Visibility = Visibility.Visible;
			}
			LoadingSection.Visibility = Visibility.Collapsed;
			commentsLoading = false;
			commentsLoaded = true;
		}

		private void CancelLoadAvatars() {
			foreach(CommentView item in CommentsListView.Items) {
				try {
					item.Cts?.Cancel();
					item.Cts?.Dispose();
				} finally {
					item.Cts = null;
				}
			}
		}

		private async void LoadAllAvatars() {
			foreach(CommentView item in CommentsListView.Items) {
				item.EnableLoadingRing();
			}
			foreach(CommentView item in CommentsListView.Items) {
				await item.LoadAvatar();
			}
		}

		private void UpdateFavoriteButton() {
			if(this.PostRef == null) {
				return;
			}
			bool enable = this.PostRef.is_favorited;
			FavoriteButton.IsChecked = enable;
			if(enable) {
				FavoriteText.Text = "Favorited";
				FavoriteIcon.Glyph = "\uEB52";
			} else {
				FavoriteText.Text = "Favorite";
				FavoriteIcon.Glyph = "\uEB51";
			}
			FavoriteListButton.IsChecked = enable;
		}

		private void UpdateDescriptionSection() {
			DescriptionText.Text = PostRef != null && !string.IsNullOrEmpty(PostRef.description) ? PostRef.description : "No Description";
			SourcesView.Items.Clear();
			if(PostRef != null) {
				foreach(string item in PostRef.sources) {
					SourcesView.Items.Add(new MyHyperLinkButton(item));
				}
				if(PostRef.sources.Count == 0) {
					SourcesText.Text = "No Source";
				} else if(PostRef.sources.Count == 1) {
					SourcesText.Text = "Source";
				} else {
					SourcesText.Text = "Sources";
				}
			} else {
				SourcesText.Text = "";
			}
		}

		private void UpdateOthers() {
			if(PostRef == null) {
				return;
			}
			bool pool = PostRef.pools != null && PostRef.pools.Count > 0;
			PoolsHintText.Visibility = !pool ? Visibility.Visible : Visibility.Collapsed;
			PoolsListView.Visibility = pool ? Visibility.Visible : Visibility.Collapsed;

			bool parent = !string.IsNullOrWhiteSpace(PostRef.relationships.parent_id);
			ParentHintText.Visibility = !parent ? Visibility.Visible : Visibility.Collapsed;
			ParentImageHolderParent.Visibility = parent ? Visibility.Visible : Visibility.Collapsed;

			bool children = PostRef.relationships.children != null && PostRef.relationships.children.Count > 0;
			ChildrenHintText.Visibility = !children ? Visibility.Visible : Visibility.Collapsed;
			ChildrenGridView.Visibility = children ? Visibility.Visible : Visibility.Collapsed;
		}

		private void MainImage_ImageOpened(object sender, RoutedEventArgs e) {
			MyProgressRing.IsActive = false;
		}

		private void MainImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) {
			Debug.WriteLine("!?!");
			//ImageTransform.ScaleX = ImageTransform.ScaleX < 2 ? 3 : 1;
			//ImageTransform.ScaleY = ImageTransform.ScaleY < 2 ? 3 : 1;
		}

		private void MainImage_PointerWheelChanged(object sender, PointerRoutedEventArgs e) {
			PointerPoint point = e.GetCurrentPoint(MainImage);
			double posX = point.Position.X;
			double posY = point.Position.Y;
			Debug.WriteLine($"{(int)posX}-{(int)posY} <=> {(int)MainImage.ActualWidth / 2}-{(int)MainImage.ActualHeight / 2}");

			double scroll = point.Properties.MouseWheelDelta > 0 ? 1.2 : 0.8;

			double newScaleX = ImageTransform.ScaleX * scroll;
			double newScaleY = ImageTransform.ScaleY * scroll;

			double newTransX = scroll > 1 ?
				(ImageTransform.TranslateX - (posX * 0.2 * ImageTransform.ScaleX)) :
				(ImageTransform.TranslateX - (posX * -0.2 * ImageTransform.ScaleX));
			double newTransY = scroll > 1 ?
				(ImageTransform.TranslateY - (posY * 0.2 * ImageTransform.ScaleY)) :
				(ImageTransform.TranslateY - (posY * -0.2 * ImageTransform.ScaleY));

			if(newScaleX < 1 || newScaleY < 1) {
				newScaleX = 1;
				newScaleY = 1;
				newTransX = 0;
				newTransY = 0;
			}
			if(newScaleX > 10 || newScaleY > 10) {
				return;
			}

			ImageTransform.ScaleX = newScaleX;
			ImageTransform.ScaleY = newScaleY;
			ImageTransform.TranslateX = newTransX;
			ImageTransform.TranslateY = newTransY;

			Limit();
		}

		private void MainImage_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) {
			ImageTransform.TranslateX += e.Delta.Translation.X;
			ImageTransform.TranslateY += e.Delta.Translation.Y;

			Limit();
		}

		private void ZoomIn() {
			Zoom(1.2);
		}

		private void ZoomOut() {
			Zoom(0.8);
		}

		private void Zoom(double ratio) {
			double newScaleX = ImageTransform.ScaleX * ratio;
			double newScaleY = ImageTransform.ScaleY * ratio;
			double newTransX = ratio > 1 ?
				(ImageTransform.TranslateX - (MainImage.ActualWidth / 2 * 0.2 * ImageTransform.ScaleX)) :
				(ImageTransform.TranslateX - (MainImage.ActualWidth / 2 * -0.2 * ImageTransform.ScaleX));
			double newTransY = ratio > 1 ?
				(ImageTransform.TranslateY - (MainImage.ActualHeight / 2 * 0.2 * ImageTransform.ScaleY)) :
				(ImageTransform.TranslateY - (MainImage.ActualHeight / 2 * -0.2 * ImageTransform.ScaleY));
			if(newScaleX < 1 || newScaleY < 1) {
				newScaleX = 1;
				newScaleY = 1;
				newTransX = 0;
				newTransY = 0;
			}
			if(newScaleX > 10 || newScaleY > 10) {
				newScaleX = 10;
				newScaleY = 10;
				return;
			}
			ImageTransform.ScaleX = newScaleX;
			ImageTransform.ScaleY = newScaleY;
			ImageTransform.TranslateX = newTransX;
			ImageTransform.TranslateY = newTransY;
			Limit();
		}

		private bool IsHorScaleAbove() {
			return MainImage.ActualWidth * ImageTransform.ScaleX > MyScrollViewer.ActualWidth;
		}

		private bool IsVerScaleAbove() {
			return MainImage.ActualHeight * ImageTransform.ScaleY > MyScrollViewer.ActualHeight;
		}

		private void Limit() {
			double horLimit = (MyScrollViewer.ActualWidth - MainImage.ActualWidth) / 2;
			if(IsHorScaleAbove()) {
				if(ImageTransform.TranslateX > -horLimit) {
					ImageTransform.TranslateX = -horLimit;
				}
				double offset = (MyScrollViewer.ActualWidth - MainImage.ActualWidth * ImageTransform.ScaleX);
				if(ImageTransform.TranslateX < offset - horLimit) {
					ImageTransform.TranslateX = offset - horLimit;
				}
			} else {
				if(ImageTransform.TranslateX < -horLimit) {
					ImageTransform.TranslateX = -horLimit;
				}
				double offset = (MyScrollViewer.ActualWidth - MainImage.ActualWidth * ImageTransform.ScaleX);
				if(ImageTransform.TranslateX > offset - horLimit) {
					ImageTransform.TranslateX = offset - horLimit;
				}
			}
			double verLimit = (MyScrollViewer.ActualHeight - MainImage.ActualHeight) / 2;
			if(IsVerScaleAbove()) {
				if(ImageTransform.TranslateY > 0) {
					ImageTransform.TranslateY = 0;
				}
				double offset = MyScrollViewer.ActualHeight - MainImage.ActualHeight * ImageTransform.ScaleY;
				if(ImageTransform.TranslateY < offset) {
					ImageTransform.TranslateY = offset;
				}
			} else {
				if(ImageTransform.TranslateY < 0) {
					ImageTransform.TranslateY = 0;
				}
				if(ImageTransform.TranslateY > verLimit) {
					ImageTransform.TranslateY = verLimit;
				}
			}
		}

		private void ResetImage() {
			ImageTransform.TranslateX = 0;
			ImageTransform.TranslateY = 0;
			ImageTransform.ScaleX = 1;
			ImageTransform.ScaleY = 1;
		}

		private void TagsListView_ItemClick(object sender, ItemClickEventArgs e) {
			string[] result_tags;
			if(LocalSettings.Current.concatTags) {
				result_tags = MainPage.GetCurrentTags().Append(((GroupTag)e.ClickedItem).Content).ToArray();
			} else {
				result_tags = new string[] { ((GroupTag)e.ClickedItem).Content };
			}
			MainPage.NavigateToPostsBrowser(1, result_tags);
		}

		private void BlackListButton_Tapped(object sender, TappedRoutedEventArgs e) {
			var btn = sender as Button;
			string tag = btn.Tag as string;
			if(Local.CheckBlackList(tag)) {
				Local.RemoveBlackList(tag);
				btn.Content = "\uF8AB";
				ToolTipService.SetToolTip(btn, "Add To BlackList");
			} else {
				Local.AddBlackList(tag);
				btn.Content = "\uEA43";
				ToolTipService.SetToolTip(btn, "Remove From BlackList");

				if(Local.CheckFollowList(tag)) {
					Local.RemoveFollowList(tag);
					var followListButton = (btn.Parent as RelativePanel).Children.OfType<Button>().ToList().Find(b => b.Name == "FollowListButton");
					followListButton.Content = "\uF8AA";
					ToolTipService.SetToolTip(followListButton, "Add To FollowList");
				}
			}
		}

		private void FollowListButton_Tapped(object sender, TappedRoutedEventArgs e) {
			var btn = sender as Button;
			string tag = btn.Tag as string;
			if(Local.CheckFollowList(tag)) {
				Local.RemoveFollowList(tag);
				btn.Content = "\uF8AA";
				ToolTipService.SetToolTip(btn, "Add To FollowList");
			} else {
				Local.AddFollowList(tag);
				btn.Content = "\uE74D";
				ToolTipService.SetToolTip(btn, "Delete From FollowList");

				if(Local.CheckBlackList(tag)) {
					Local.RemoveBlackList(tag);
					var blackListButton = (btn.Parent as RelativePanel).Children.OfType<Button>().ToList().Find(b => b.Name == "BlackListButton");
					blackListButton.Content = "\uF8AB";
					ToolTipService.SetToolTip(blackListButton, "Add To BlackList");
				}
			}
		}

		private void BlackListButton_Loaded(object sender, RoutedEventArgs e) {
			var btn = sender as Button;
			string tag = btn.Tag as string;
			if(Local.CheckBlackList(tag)) {
				btn.Content = "\uEA43";
				ToolTipService.SetToolTip(btn, "Remove From BlackList");
			} else {
				btn.Content = "\uF8AB";
				ToolTipService.SetToolTip(btn, "Add To BlackList");
			}
		}

		private void FollowListButton_Loaded(object sender, RoutedEventArgs e) {
			var btn = sender as Button;
			string tag = btn.Tag as string;
			if(Local.CheckFollowList(tag)) {
				btn.Content = "\uE74D";
				ToolTipService.SetToolTip(btn, "Delete From FollowList");
			} else {
				btn.Content = "\uF8AA";
				ToolTipService.SetToolTip(btn, "Add To FollowList");
			}
		}

		private async void DownloadButton_Tapped(object sender, TappedRoutedEventArgs e) {
			if(await DownloadsManager.CheckDownloadAvailableWithDialog()) {
				if(await DownloadsManager.RegisterDownload(PostRef)) {
					MainPage.CreateTip_SuccessDownload(this);
				} else {
					await MainPage.CreatePopupDialog("Error", "Downloads Failed");
				}
			}
		}

		private void MoreInfoButton_Tapped(object sender, TappedRoutedEventArgs e) {
			MainSplitView.IsPaneOpen = !MainSplitView.IsPaneOpen;
		}

		private async void InfoButton_Tapped(object sender, TappedRoutedEventArgs e) {
			string tag = (sender as Button).Tag as string;
			string name = tag;
			var dialog = new ContentDialog() {
				Title = $"Tag Information: {name}",
				CloseButtonText = "Back",
			};
			dialog.Content = new TagInformationDisplay(tags_pool, tag);
			await dialog.ShowAsync();
		}

		private void SplitViewModeSwitch_Toggled(object sender, RoutedEventArgs e) {
			if(MainSplitView == null) {
				return;
			}
			MainSplitView.DisplayMode = SplitViewModeSwitch.IsOn ? SplitViewDisplayMode.Overlay : SplitViewDisplayMode.Inline;
		}

		private void LeftButton_PointerEntered(object sender, PointerRoutedEventArgs e) {
			(sender as Button).Opacity = 1;
		}

		private void LeftButton_PointerExited(object sender, PointerRoutedEventArgs e) {
			(sender as Button).Opacity = 0.2;
		}

		private void LeftButton_Tapped(object sender, TappedRoutedEventArgs e) {
			GoLeft();
		}

		private void RightButton_PointerEntered(object sender, PointerRoutedEventArgs e) {
			(sender as Button).Opacity = 1;
		}

		private void RightButton_PointerExited(object sender, PointerRoutedEventArgs e) {
			(sender as Button).Opacity = 0.2;
		}

		private void RightButton_Tapped(object sender, TappedRoutedEventArgs e) {
			GoRight();
		}

		private void GoLeft() {
			MainPage.NavigateToPicturePage(App.postsList.GoLeft());
		}

		private void GoRight() {
			MainPage.NavigateToPicturePage(App.postsList.GoRight());
		}

		private void InformationPivot_SelectionChanged(object sender, SelectionChangedEventArgs e) {
			if(e.AddedItems != null && e.AddedItems.Count > 0 && e.AddedItems[0] is PivotItem item && item.Header as string == "Comments" && !commentsLoaded && !commentsLoading) {
				if(PostRef != null) {
					LoadCommentsAsync();
				} else {
					CommentsHint.Visibility = Visibility.Visible;
				}
			}
		}

		private async void MoreInfoItem_Click(object sender, RoutedEventArgs e) {
			if(PostRef == null) {
				return;
			}
			var dialog = new ContentDialog() {
				Title = "More Info",
				Content = new PostMoreInfoDialog(PostRef),
				PrimaryButtonText = "Back",
			};
			await dialog.ShowAsync();
		}

		private async void BrowserItem_Click(object sender, RoutedEventArgs e) {
			if(PostRef == null) {
				return;
			}
			if(!await Launcher.LaunchUriAsync(new Uri($"https://{Data.GetHost()}/posts/{PostRef.id}"))) {
				await MainPage.CreatePopupDialog("Error", "Could not Open Default Browser");
			}
		}

		private void CopyItem_Click(object sender, RoutedEventArgs e) {
			if(PostRef == null) {
				return;
			}
			var dataPackage = new DataPackage() {
				RequestedOperation = DataPackageOperation.Copy
			};
			dataPackage.SetText($"{PostRef.id}");
			Clipboard.SetContent(dataPackage);
		}

		private void CopyImageItem_Click(object sender, RoutedEventArgs e) {
			if(imageDataPackage == null) {
				return;
			}
			Clipboard.SetContent(imageDataPackage);
		}

		private async void DebugItem_Click(object sender, RoutedEventArgs e) {
			if(PostRef == null) {
				return;
			}
			var dialog = new ContentDialog() {
				Title = "Debug Info",
				Content = new PostDebugView(PostRef),
				PrimaryButtonText = "Back",
			};
			await dialog.ShowAsync();
		}

		private async void FavoriteButton_Tapped(object sender, TappedRoutedEventArgs e) {
			e.Handled = true;
			FavoriteButton.IsEnabled = false;
			FavoriteListButton.IsEnabled = false;
			FavoriteText.Text = "Pending";
			FavoriteIcon.Glyph = "\uE10C";
			if(FavoriteButton.IsChecked.Value) {
				HttpResult<string> result = await Favorites.PostAsync(this.PostRef.id);
				if(result.Result == HttpResultType.Success) {
					FavoriteText.Text = "Favorited";
					FavoriteIcon.Glyph = "\uEB52";
				} else {
					FavoriteText.Text = "Favorite";
					FavoriteIcon.Glyph = "\uEB51";
					MainPage.CreateTip(this, result.StatusCode.ToString(), result.Helper, Symbol.Important, "OK");
					FavoriteButton.IsChecked = false;
				}
			} else {
				HttpResult<string> result = await Favorites.DeleteAsync(this.PostRef.id);
				if(result.Result == HttpResultType.Success) {
					FavoriteText.Text = "Favorite";
					FavoriteIcon.Glyph = "\uEB51";
				} else {
					FavoriteText.Text = "Favorited";
					FavoriteIcon.Glyph = "\uEB52";
					MainPage.CreateTip(this, result.StatusCode.ToString(), result.Helper, Symbol.Important, "OK");
					FavoriteButton.IsChecked = true;
				}
			}
			FavoriteButton.IsEnabled = true;
			FavoriteListButton.IsEnabled = true;
			FavoriteListButton.IsChecked = FavoriteButton.IsChecked;
		}

		private void ToggleTagsButton_Tapped(object sender, TappedRoutedEventArgs e) {
			double from = TagsListView.Width;
			double to;
			if(TagsListView.Width <= 125) {
				to = 250;
				ToggleTagsButtonIcon.Glyph = "\uE8A0";
			} else {
				to = 0;
				ToggleTagsButtonIcon.Glyph = "\uE89F";
			}
			TagsDisplay.Children[0].SetValue(DoubleAnimation.FromProperty, from);
			TagsDisplay.Children[0].SetValue(DoubleAnimation.ToProperty, to);
			TagsDisplay.Begin();
		}

		private void GotoLibraryButton_Tapped(object sender, TappedRoutedEventArgs e) {
			MainPage.NavigateTo(PageTag.Library);
		}

		private void GotoHomeButton_Tapped(object sender, TappedRoutedEventArgs e) {
			MainPage.NavigateTo(PageTag.PostsBrowser);
		}

		private void RelativePanel_RightTapped(object sender, RightTappedRoutedEventArgs e) {
			string tag = (string)((Panel)sender).Tag;
			MenuFlyout flyout = new MenuFlyout();
			MenuFlyoutItem item_copy = new MenuFlyoutItem() {
				Text = "Copy Tag",
				Icon = new FontIcon() { Glyph = "\uE8C8" },
			};
			item_copy.Click += (s, arg) => {
				if(PostRef == null) {
					return;
				}
				var dataPackage = new DataPackage() {
					RequestedOperation = DataPackageOperation.Copy
				};
				dataPackage.SetText(tag);
				Clipboard.SetContent(dataPackage);
			};
			flyout.Items.Add(item_copy);
			MenuFlyoutItem item_concat = new MenuFlyoutItem() {
				Text = "Concat Search",
				Icon = new FontIcon() { Glyph = "\uE109" },
			};
			item_concat.Click += (s, arg) => {
				string[] concat_tags = MainPage.GetCurrentTags().Append(tag).ToArray();
				MainPage.NavigateToPostsBrowser(1, concat_tags);
			};
			flyout.Items.Add(item_concat);
			MenuFlyoutItem item_overlay = new MenuFlyoutItem() {
				Text = "Overlay Search",
				Icon = new FontIcon() { Glyph = "\uE71E" },
			};
			item_overlay.Click += (s, arg) => {
				MainPage.NavigateToPostsBrowser(1, tag);
			};
			flyout.Items.Add(item_overlay);
			if(flyout.Items.Count != 0) {
				FrameworkElement senderElement = sender as FrameworkElement;
				var p = e.GetPosition(sender as UIElement);
				flyout.ShowAt(sender as UIElement, p);
			}
		}

		private async void PoolsListView_ItemClick(object sender, ItemClickEventArgs e) {
			MainPage.CreateInstantDialog("Please Wait", "Loading Pool");
			E621Pool pool = await E621Pool.GetAsync((string)e.ClickedItem);
			MainPage.HideInstantDialog();
			MainPage.NavigateToPostsBrowser(pool);
		}

		private void FavoriteListButton_Tapped(object sender, TappedRoutedEventArgs e) {
			var flyout = new Flyout();
			var content = new PersonalFavoritesList(flyout, PostType, path) {
				Width = 200,
				IsInitialFavorited = this.PostRef.is_favorited,
			};
			flyout.Content = content;
			Point pos = e.GetPosition(sender as UIElement);
			pos.Y += 20;
			TextInputing = true;
			flyout.ShowAt(sender as UIElement, new FlyoutShowOptions() {
				Placement = FlyoutPlacementMode.Bottom,
				ShowMode = FlyoutShowMode.Auto,
				Position = pos,
			});
			flyout.Closed += (s, args) => TextInputing = false;
		}
	}

	public class GroupTagListWithColor: ObservableCollection<GroupTag> {
		public string Key { get; set; }
		public GroupTagListWithColor(string key) : base() {
			this.Key = key;
		}
		public GroupTagListWithColor(string key, List<GroupTag> content) : base() {
			this.Key = key;
			content.ForEach(s => this.Add(s));
		}
	}

	public struct GroupTag {
		public string Content { get; set; }
		public Color Color { get; set; }
		public Brush Brush => new SolidColorBrush(this.Color);
		public GroupTag(string content, Color color) {
			Content = content;
			Color = color;
		}
	}

	public class GroupTagList: ObservableCollection<string> {
		public string Key { get; set; }
		public GroupTagList(string key) : base() {
			this.Key = key;
		}
		public GroupTagList(string key, List<string> content) : base() {
			this.Key = key;
			content.ForEach(s => this.Add(s));
		}
	}

	public enum PathType {
		PostID, Local
	}

	public enum FileType {
		Png, Jpg, Gif, Webm, Anim
	}


	public interface ILocalImage {
		Post ImagePost { get; }
		StorageFile ImageFile { get; }
	}

	public class MixPost: ILocalImage {
		public PathType Type { get; set; }
		public string ID { get; set; }
		public string LocalPath { get; set; }
		// -------------------------
		public Post PostRef { get; set; }
		// -------------------------
		public StorageFile ImageFile { get; set; }
		public MetaFile MetaFile { get; set; }
		// -------------------------
		public bool LocalLoaded => ImageFile != null && MetaFile != null;
		Post ILocalImage.ImagePost => MetaFile.MyPost;
		StorageFile ILocalImage.ImageFile => ImageFile;

		public MixPost(PathType pathType, string path) {
			Type = pathType;
			switch(Type) {
				case PathType.PostID:
					ID = path;
					break;
				case PathType.Local:
					LocalPath = path;
					break;
				default:
					throw new PathTypeException();
			}
		}

		public void Finish(Post post) {
			if(Type != PathType.PostID) {
				throw new Exception();
			}
			PostRef = post;
		}

		public void Finish(StorageFile storageFile, MetaFile metaFile) {
			if(Type != PathType.Local) {
				throw new Exception();
			}
			ImageFile = storageFile;
			MetaFile = metaFile;
		}
	}
}
