﻿using E621Downloader.Models.Posts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace E621Downloader.Views.TagsManagementSection {
	public sealed partial class TagsSelectionView: Page {
		private readonly ContentDialog dialog;
		public ResultType Result { get; private set; } = ResultType.None;
		private readonly Dictionary<string, E621Tag> tags_pool = new Dictionary<string, E621Tag>();
		private readonly List<string> currentTags = new List<string>();

		public TagsSelectionView(ContentDialog dialog, string[] tags) {
			this.InitializeComponent();
			this.dialog = dialog;
			foreach(string item in tags) {
				MySuggestBox.Text += item + " ";
			}
			MySuggestBox.Text = MySuggestBox.Text.Trim();
			MySuggestBox.SelectionStart = MySuggestBox.Text.Length;
		}

		private bool itemClick = false;
		private void AutoCompletesListView_ItemClick(object sender, ItemClickEventArgs e) {
			var item = (SingleTagSuggestion)e.ClickedItem;
			var tag = item.CompleteName;
			//change last
			//var last = GetLast(MySuggestBox.Text);
			int lastSpace = MySuggestBox.Text.LastIndexOf(' ');
			if(lastSpace == -1) {
				MySuggestBox.Text = tag;
			} else {
				//MySuggestBox.Text = MySuggestBox.Text.Trim() + " " + tag;
				string cut = MySuggestBox.Text.Substring(0, lastSpace).Trim();
				MySuggestBox.Text = cut + " " + tag;
			}
			AutoCompletesListView.Items.Clear();
			CalculateCurrentTags();
			itemClick = true;
		}

		private void MySuggestBox_TextChanged(object sender, TextChangedEventArgs e) {
			if(itemClick) {
				itemClick = false;
				return;
			}
			var box = (TextBox)sender;
			if(box.Text.LastOrDefault() == ' ') {
				AutoCompletesListView.Items.Clear();
				return;
			}
			CalculateCurrentTags();
			string last = currentTags.LastOrDefault();
			//if(args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) {
			if(string.IsNullOrWhiteSpace(last)) {
				AutoCompletesListView.Items.Clear();
			} else {
				//await LoadAutoSuggestion(last);
				SetLoadingbar(false);
				DelayLoad(last);
			}
			//}
		}
		private CancellationTokenSource delay_cts;
		private async void DelayLoad(string tag) {
			if(delay_cts != null) {
				delay_cts.Cancel();
				delay_cts.Dispose();
			}
			delay_cts = new CancellationTokenSource();
			try {
				await Task.Delay(500, delay_cts.Token);
			} catch(TaskCanceledException) {
				return;
			}
			await LoadAutoSuggestion(tag);
		}

		private CancellationTokenSource cts;
		private async Task LoadAutoSuggestion(string tag) {
			try {
				if(cts != null) {
					cts.Cancel();
					cts.Dispose();
				}
			} catch { }
			cts = new CancellationTokenSource();
			SetLoadingbar(true);
			AutoCompletesListView.Items.Clear();
			E621AutoComplete[] acs = await E621AutoComplete.GetAsync(tag, cts.Token);
			AutoCompletesListView.Items.Clear();
			//if(acs == null || acs.Length == 0) {
			//	SetLoadingbar(false);
			//	return;
			//}
			foreach(E621AutoComplete item in acs) {
				AutoCompletesListView.Items.Add(new SingleTagSuggestion(item));
			}
			var last = AutoCompletesListView.Items.Cast<SingleTagSuggestion>().LastOrDefault();
			if(last != null) {
				last.Loaded += (s, e) => {
					SetLoadingbar(false);
				};
			}
		}

		private void CalculateCurrentTags() {
			currentTags.Clear();
			foreach(string item in MySuggestBox.Text.Trim().Split(" ").Where(s => !string.IsNullOrEmpty(s)).ToList()) {
				currentTags.Add(item);
			}
		}

		private void SetLoadingbar(bool active) {
			LoadingBar.IsIndeterminate = active;
			LoadingBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
		}

		public E621Tag GetE621Tag(string tag) {
			return tags_pool.ContainsKey(tag) ? tags_pool[tag] : null;
		}

		public void RegisterE621Tag(string tag, E621Tag e621tag) {
			if(tags_pool.ContainsKey(tag)) {
				tags_pool[tag] = e621tag;
			} else {
				tags_pool.Add(tag, e621tag);
			}
		}


		public void RemoveTag(string tag) {
			MySuggestBox.Text = MySuggestBox.Text.Replace(tag, "").Trim();
		}

		public string[] GetTags() => currentTags.ToArray();

		private void DialogBackButton_Tapped(object sender, TappedRoutedEventArgs e) {
			Result = ResultType.None;
			Hide();
		}

		private void HotButton_Tapped(object sender, TappedRoutedEventArgs e) {
			Result = ResultType.Hot;
			Hide();
		}

		private void RandomButton_Tapped(object sender, TappedRoutedEventArgs e) {
			Result = ResultType.Random;
			Hide();
		}

		//private string GetLast(string value) {
		//	int lastSpace = value.LastIndexOf(' ');
		//	if(lastSpace != -1) {
		//		return value.Substring(lastSpace, value.Length - lastSpace).Trim();
		//	} else {
		//		return value;
		//	}
		//}

		public enum ResultType {
			None, Search, Hot, Random
		}

		private void MySuggestBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e) {
			if(e.Key == VirtualKey.Enter) {
				Result = ResultType.Search;
				Hide();
			} else if(e.Key == VirtualKey.Escape) {
				Result = ResultType.None;
				Hide();
			}
		}

		private void SearchButton_Tapped(object sender, TappedRoutedEventArgs e) {
			Result = ResultType.Search;
			Hide();
		}

		private void Hide() {
			if(cts != null) {
				cts.Cancel();
				cts.Dispose();
			}
			dialog.Hide();
		}
	}
}
