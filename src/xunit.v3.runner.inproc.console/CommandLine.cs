using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Xunit.Internal;
using Xunit.Runner.Common;

namespace Xunit.Runner.InProc.SystemConsole;

/// <summary/>
public class CommandLine : CommandLineParserBase
{
	readonly Assembly assembly;
	readonly string? assemblyFileName;

	/// <summary/>
	public CommandLine(
		Assembly assembly,
		string[] args,
		IReadOnlyList<IRunnerReporter>? runnerReporters = null,
		string? reporterFolder = null)
			: base(runnerReporters, reporterFolder ?? Path.GetDirectoryName(assembly.GetSafeLocation()), args)
	{
		this.assembly = assembly;
		assemblyFileName = assembly.GetSafeLocation();

		// General options
		AddParser(
			"parallel", OnParallel, CommandLineGroup.General, "<option>",
			"set parallelization based on option",
			"  none        - turn off all parallelization",
			"  collections - only parallelize collections"
		);
		AddParser(
			"tcp", OnTcp, CommandLineGroup.General, "<port>",
			"launches in v3 child process mode, connecting to the given",
			"TCP port (on localhost) for IPC"
		);

		// Move options that aren't compatible with -tcp
		MoveParser("debug", CommandLineGroup.Interactive);
		MoveParser("noautoreporters", CommandLineGroup.Interactive);
		MoveParser("pause", CommandLineGroup.Interactive);
		MoveParser("wait", CommandLineGroup.Interactive);
	}

	void AddAssembly(
		Assembly assembly,
		string? assemblyFileName,
		string? configFileName)
	{
		if (assemblyFileName != null && !FileExists(assemblyFileName))
			throw new ArgumentException($"assembly not found: {assemblyFileName}");
		if (configFileName != null && !FileExists(configFileName))
			throw new ArgumentException($"config file not found: {configFileName}");

		var targetFramework = assembly.GetTargetFramework();
		var projectAssembly = new XunitProjectAssembly(Project)
		{
			Assembly = assembly,
			AssemblyFileName = GetFullPath(assemblyFileName),
			ConfigFileName = GetFullPath(configFileName),
			TargetFramework = targetFramework
		};

		ConfigReader_Json.Load(projectAssembly.Configuration, projectAssembly.AssemblyFileName, projectAssembly.ConfigFileName);

		Project.Add(projectAssembly);
	}

	/// <summary/>
	protected override Assembly LoadAssembly(string dllFile) =>
#if NETFRAMEWORK
		Assembly.LoadFile(dllFile);
#else
		Assembly.Load(new AssemblyName(Path.GetFileNameWithoutExtension(dllFile)));
#endif

	/// <summary/>
	protected void OnTcp(KeyValuePair<string, string?> option)
	{
		if (option.Value == null)
			throw new ArgumentException("missing argument for -tcp");

		if (!int.TryParse(option.Value, out var port) || port < 1024 || port > 65535)
			throw new ArgumentException($"incorrect argument value for -tcp (must be an integer between 1024 and 65535)");

		Project.Configuration.TcpPort = port;
	}

	/// <summary/>
	public XunitProject Parse()
	{
		if (Project.Assemblies.Count > 0)
			throw new InvalidOperationException("Parse may only be called once");

		var argsStartIndex = 0;

		string? configFileName = null;
		if (Args.Length > 0 && !Args[0].StartsWith("-") && Args[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			configFileName = Args[0];
			argsStartIndex = 1;
		}

		AddAssembly(assembly, assemblyFileName, configFileName);

		return ParseInternal(argsStartIndex);
	}
}
