using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;

namespace WpfTaskBar;

public class ProcessUtility
{
	/// <summary>
	/// 指定されたプロセスIDに対する子プロセスを列挙します。
	/// </summary>
	/// <param name="parentProcessId">親プロセスID</param>
	/// <returns>子プロセスのIDリスト</returns>
	public static List<int> GetChildProcessIds(int parentProcessId)
	{
		List<int> childProcessIds = new List<int>();

		// WMI（Windows Management Instrumentation）を使用して親子関係のあるプロセスを検索
		string queryString = $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentProcessId}";

		using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(queryString))
		{
			using (ManagementObjectCollection results = searcher.Get())
			{
				foreach (ManagementObject process in results)
				{
					// 子プロセスIDを取得
					int childProcessId = Convert.ToInt32(process["ProcessId"]);
					childProcessIds.Add(childProcessId);
				}
			}
		}

		return childProcessIds;
	}

	/// <summary>
	/// 指定されたプロセスIDに対する子孫プロセス（子、孫、...）を再帰的に列挙します。
	/// </summary>
	/// <param name="parentProcessId">親プロセスID</param>
	/// <returns>子孫プロセスのIDリスト</returns>
	public static List<int> GetDescendantProcessIds(int parentProcessId)
	{
		List<int> descendantProcessIds = new List<int>();
		List<int> childProcessIds = GetChildProcessIds(parentProcessId);

		// 子プロセスを追加
		descendantProcessIds.AddRange(childProcessIds);

		// 各子プロセスに対して再帰的に孫プロセスを検索
		foreach (int childProcessId in childProcessIds)
		{
			List<int> grandChildProcessIds = GetDescendantProcessIds(childProcessId);
			descendantProcessIds.AddRange(grandChildProcessIds);
		}

		return descendantProcessIds;
	}

	/// <summary>
	/// 使用例: プロセスIDから子プロセス情報を表示
	/// </summary>
	public static void DisplayChildProcessInfo(int parentProcessId)
	{
		try
		{
			Console.WriteLine($"親プロセスID: {parentProcessId}");

			List<int> childProcessIds = GetChildProcessIds(parentProcessId);

			if (childProcessIds.Count == 0)
			{
				Console.WriteLine("子プロセスはありません。");
				return;
			}

			Console.WriteLine($"子プロセス数: {childProcessIds.Count}");
			Console.WriteLine("子プロセス一覧:");

			foreach (int processId in childProcessIds)
			{
				try
				{
					Process process = Process.GetProcessById(processId);
					Console.WriteLine($"  PID: {processId}, 名前: {process.ProcessName}");
				}
				catch (ArgumentException)
				{
					Console.WriteLine($"  PID: {processId}, 名前: [既に終了したプロセス]");
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"エラーが発生しました: {ex.Message}");
		}
	}
}