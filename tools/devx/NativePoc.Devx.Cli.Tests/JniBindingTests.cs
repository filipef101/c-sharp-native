using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NativePoc.ReloadGen;

namespace NativePoc.Devx.Tests;

public sealed class JniBindingTests
{
	[Fact]
	public void A_declared_binding_emits_jni_without_reflection()
	{
		var source = """
			using System;
			namespace NativePoc
			{
				[AttributeUsage(AttributeTargets.Class)]
				public sealed class JniTypeAttribute : Attribute
				{
					public JniTypeAttribute(string className) => ClassName = className;
					public string ClassName { get; }
				}

				[AttributeUsage(AttributeTargets.Method)]
				public sealed class JniMethodAttribute : Attribute
				{
					public JniMethodAttribute(string name, string signature)
					{
						Name = name;
						Signature = signature;
					}
					public string Name { get; }
					public string Signature { get; }
					public bool Instance { get; set; }
					public string Service { get; set; } = "";
				}

				[JniType("android/os/Vibrator")]
				static partial class DeviceVibrator
				{
					[JniMethod("vibrate", "(J)V", Instance = true, Service = "vibrator")]
					public static partial void Vibrate(long milliseconds);
				}
			}
			""";
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create(
			"bind-test",
			[tree],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		var generated = JniBindingGenerator.Emit(compilation);
		Assert.Contains("JNIEnv.FindClass(\"android/os/Vibrator\")", generated, StringComparison.Ordinal);
		Assert.Contains("JNIEnv.GetMethodID(jniClass, \"vibrate\", \"(J)V\")", generated, StringComparison.Ordinal);
		Assert.Contains("JNIEnv.CallVoidMethod(service.Handle, jniMethod", generated, StringComparison.Ordinal);
		Assert.Contains("jni-bind class=android/os/Vibrator method=vibrate", generated, StringComparison.Ordinal);
		Assert.Contains("JniRegistry.Add(\"DeviceVibrator.Vibrate\", args => Vibrate(long.Parse(args[0], CultureInfo.InvariantCulture)));", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("GetMethod(", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("Type.GetType", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("Assembly.Load", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void A_method_with_a_body_registers_without_a_hand_written_initializer()
	{
		var source = """
			using System;
			namespace NativePoc
			{
				[AttributeUsage(AttributeTargets.Class)]
				public sealed class JniTypeAttribute : Attribute
				{
					public JniTypeAttribute(string className) => ClassName = className;
					public string ClassName { get; }
				}

				[AttributeUsage(AttributeTargets.Method)]
				public sealed class JniMethodAttribute : Attribute
				{
					public JniMethodAttribute(string name, string signature)
					{
						Name = name;
						Signature = signature;
					}
					public string Name { get; }
					public string Signature { get; }
				}

				[JniType("android/widget/Toast")]
				static partial class DeviceToast
				{
					[JniMethod("makeText", "(Landroid/content/Context;Ljava/lang/CharSequence;I)Landroid/widget/Toast;")]
					public static void Show(string text)
					{
						_ = text;
					}
				}
			}
			""";
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create(
			"toast-test",
			[tree],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		var generated = JniBindingGenerator.Emit(compilation);
		Assert.Contains("JniRegistry.Add(\"DeviceToast.Show\", args => Show(args.Length > 0 ? args[0] : \"\"));", generated, StringComparison.Ordinal);
		Assert.Contains("RegisterJni()", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("JNIEnv.FindClass", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("public static void Show", generated, StringComparison.Ordinal);
	}

	[Fact]
	public void A_blocking_body_is_marked_and_not_a_jni_env_call()
	{
		const string source = """
			using System;
			namespace NativePoc
			{
				[AttributeUsage(AttributeTargets.Class)]
				public sealed class JniTypeAttribute : Attribute
				{
					public JniTypeAttribute(string className) => ClassName = className;
					public string ClassName { get; }
				}

				[AttributeUsage(AttributeTargets.Method)]
				public sealed class JniMethodAttribute : Attribute
				{
					public JniMethodAttribute(string name, string signature)
					{
						Name = name;
						Signature = signature;
					}
					public string Name { get; }
					public string Signature { get; }
					public bool Blocking { get; set; }
				}

				[JniType("android/os/BatteryManager")]
				static partial class DeviceBattery
				{
					[JniMethod("getIntProperty", "(I)I", Blocking = true)]
					public static string Level() => "1";
				}
			}
			""";
		var tree = CSharpSyntaxTree.ParseText(source);
		var compilation = CSharpCompilation.Create(
			"battery-test",
			[tree],
			[MetadataReference.CreateFromFile(typeof(object).Assembly.Location), MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location)],
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		var generated = JniBindingGenerator.Emit(compilation);
		Assert.Contains("JniRegistry.Add(\"DeviceBattery.Level\", args => Level());", generated, StringComparison.Ordinal);
		Assert.Contains("JniRegistry.MarkBlocking(\"DeviceBattery.Level\");", generated, StringComparison.Ordinal);
		Assert.DoesNotContain("JNIEnv.FindClass", generated, StringComparison.Ordinal);
	}
}
