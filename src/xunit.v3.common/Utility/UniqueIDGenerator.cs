using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using Xunit.Internal;
using Xunit.v3;

namespace Xunit.Sdk
{
	/// <summary>
	/// Generates unique IDs from multiple string inputs. Used to compute the unique
	/// IDs that are used inside the test framework.
	/// </summary>
	public class UniqueIDGenerator : IDisposable
	{
		// ObjectIDGenerator creates a unique ID for every instance you give it.
		// These ID's are unique per instance of ObjectIDGenerator.
		static ObjectIDGenerator idGenerator = new ObjectIDGenerator();
		static Dictionary<Type, Func<object, byte[]>> primitives =
			new Dictionary<Type, Func<object, byte[]>>();
		bool disposed;
		HashAlgorithm hasher;
		Stream stream;

		static UniqueIDGenerator()
		{
			// These sizes are only known at runtime.
			primitives[typeof(string)] = (obj) => Encoding.UTF8.GetBytes((string)obj);
			primitives[typeof(bool)] = (obj) => BitConverter.GetBytes((bool)obj);
			primitives[typeof(char)] = (obj) => BitConverter.GetBytes((char)obj);
			primitives[typeof(byte)] = (obj) => BitConverter.GetBytes((byte)obj);
			primitives[typeof(sbyte)] = (obj) => BitConverter.GetBytes((sbyte)obj);
			primitives[typeof(short)] = (obj) => BitConverter.GetBytes((short)obj);
			primitives[typeof(ushort)] = (obj) => BitConverter.GetBytes((ushort)obj);
			primitives[typeof(int)] = (obj) => BitConverter.GetBytes((int)obj);
			primitives[typeof(uint)] = (obj) => BitConverter.GetBytes((uint)obj);
			primitives[typeof(nint)] = (obj) => BitConverter.GetBytes((nint)obj);
			primitives[typeof(nuint)] = (obj) => BitConverter.GetBytes((nuint)obj);
			primitives[typeof(IntPtr)] = (obj) => BitConverter.GetBytes((nint)new IntPtr((nint)obj));
			primitives[typeof(UIntPtr)] = (obj) => BitConverter.GetBytes((nuint)new UIntPtr((nuint)obj));
			primitives[typeof(long)] = (obj) => BitConverter.GetBytes((long)obj);
			primitives[typeof(ulong)] = (obj) => BitConverter.GetBytes((ulong)obj);
			primitives[typeof(float)] = (obj) => BitConverter.GetBytes((float)obj);
			primitives[typeof(double)] = (obj) => BitConverter.GetBytes((double)obj);
			primitives[typeof(decimal)] = (obj) =>
			{
				decimal dec = (decimal)obj;
				string str = dec.ToString();
				return Encoding.UTF8.GetBytes(str);
			};
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="UniqueIDGenerator"/> class.
		/// </summary>
		public UniqueIDGenerator()
		{
			hasher = SHA256.Create();
			stream = new MemoryStream();
		}

		/// <summary>
		/// Add a string value into the unique ID computation.
		/// </summary>
		/// <param name="value">The string value to be added to the unique ID computation</param>
		public void Add(string value)
		{
			Guard.ArgumentNotNull(value);

			var bytes = Encoding.UTF8.GetBytes(value);
			AddByteArray(bytes);
		}

		/// <summary>
		/// Add a byte array value into the unique ID computation.
		/// </summary>
		/// <param name="bytes">The byte array to be added to the unique ID computation</param>
		public void AddByteArray(byte[] bytes)
		{
			Guard.ArgumentNotNull(bytes);

			lock (stream)
			{
				if (disposed)
					throw new ObjectDisposedException(nameof(UniqueIDGenerator), "Cannot use UniqueIDGenerator after you have called Compute or Dispose");

				stream.Write(bytes, 0, bytes.Length);
				stream.WriteByte(0);
			}
		}

		/// <summary>
		/// Compute the unique ID for the given input values. Note that once the unique
		/// ID has been computed, no further <see cref="Add"/> operations will be allowed.
		/// </summary>
		/// <returns>The computed unique ID</returns>
		public string Compute()
		{
			lock (stream)
			{
				if (disposed)
					throw new ObjectDisposedException(nameof(UniqueIDGenerator), "Cannot use UniqueIDGenerator after you have called Compute or Dispose");

				stream.Seek(0, SeekOrigin.Begin);

				var hashBytes = hasher.ComputeHash(stream);

				Dispose();

				return ToHexString(hashBytes);
			}
		}

		/// <inheritdoc/>
		public void Dispose()
		{
			lock (stream)
			{
				if (!disposed)
				{
					disposed = true;
					stream?.Dispose();
					hasher?.Dispose();
				}
			}
		}

		/// <summary>
		/// Computes a unique ID for an assembly, to be placed into <see cref="_TestAssemblyMessage.AssemblyUniqueID"/>
		/// </summary>
		/// <param name="assemblyName">The assembly name</param>
		/// <param name="assemblyPath">The optional assembly path</param>
		/// <param name="configFilePath">The optional configuration file path</param>
		/// <returns>The computed unique ID for the assembly</returns>
		public static string ForAssembly(
			string assemblyName,
			string? assemblyPath,
			string? configFilePath)
		{
			Guard.ArgumentNotNullOrEmpty(assemblyName);

			var parsedAssemblyName = new AssemblyName(assemblyName);
			Guard.ArgumentNotNull("assemblyName must include a name component", parsedAssemblyName.Name, nameof(assemblyName));

			using var generator = new UniqueIDGenerator();
			generator.Add(parsedAssemblyName.Name);
			generator.Add(assemblyPath ?? string.Empty);
			generator.Add(configFilePath ?? string.Empty);
			return generator.Compute();
		}

		/// <summary>
		/// Computes a unique ID for a test, to be placed into <see cref="_TestMessage.TestUniqueID"/>
		/// </summary>
		/// <param name="testCaseUniqueID">The unique ID of the test case that this test belongs to.</param>
		/// <param name="testIndex">The index of this test in the test case, typically starting with 0
		/// (though a negative number may be used to prevent collisions with legitimate test indices).</param>
		public static string ForTest(
			string testCaseUniqueID,
			int testIndex)
		{
			Guard.ArgumentNotNull(testCaseUniqueID);

			using var generator = new UniqueIDGenerator();
			generator.Add(testCaseUniqueID);
			generator.Add(testIndex.ToString());
			return generator.Compute();
		}

		/// <summary>
		/// Computes a unique ID for a test case, to be placed into <see cref="_TestCaseMessage.TestCaseUniqueID"/>
		/// </summary>
		/// <param name="parentUniqueID">The unique ID of the parent in the hierarchy; typically the test method
		/// unique ID, but may also be the test class or test collection unique ID, when test method (and
		/// possibly test class) don't exist.</param>
		/// <param name="testMethodGenericTypes">The test method's generic types</param>
		/// <param name="testMethodArguments">The test method's arguments</param>
		/// <returns>The computed unique ID for the test case</returns>
		public static string ForTestCase(
			string parentUniqueID,
			_ITypeInfo[]? testMethodGenericTypes,
			object?[]? testMethodArguments)
		{
			Guard.ArgumentNotNull(parentUniqueID);

			using var generator = new UniqueIDGenerator();

			generator.Add(parentUniqueID);
			generator.AddObjectValue(testMethodArguments);

			if (testMethodGenericTypes != null)
				for (var idx = 0; idx < testMethodGenericTypes.Length; idx++)
					generator.Add(TypeUtility.ConvertToSimpleTypeName(testMethodGenericTypes[idx]));

			return generator.Compute();
		}

		/// <summary>
		/// Computes a unique ID for a test class, to be placed into <see cref="_TestClassMessage.TestClassUniqueID"/>
		/// </summary>
		/// <param name="testCollectionUniqueID">The unique ID of the parent test collection for the test class</param>
		/// <param name="className">The optional fully qualified type name of the test class</param>
		/// <returns>The computed unique ID for the test class (may return <c>null</c> if <paramref name="className"/>
		/// is null)</returns>
		[return: NotNullIfNotNull("className")]
		public static string? ForTestClass(
			string testCollectionUniqueID,
			string? className)
		{
			Guard.ArgumentNotNull(testCollectionUniqueID);

			if (className == null)
				return null;

			using var generator = new UniqueIDGenerator();
			generator.Add(testCollectionUniqueID);
			generator.Add(className);
			return generator.Compute();
		}

		/// <summary>
		/// Computes a unique ID for a test collection, to be placed into <see cref="_TestCollectionMessage.TestCollectionUniqueID"/>
		/// </summary>
		/// <param name="assemblyUniqueID">The unique ID of the assembly the test collection lives in</param>
		/// <param name="collectionDisplayName">The display name of the test collection</param>
		/// <param name="collectionDefinitionClassName">The optional class name that contains the test collection definition</param>
		/// <returns>The computed unique ID for the test collection</returns>
		public static string ForTestCollection(
			string assemblyUniqueID,
			string collectionDisplayName,
			string? collectionDefinitionClassName)
		{
			Guard.ArgumentNotNull(assemblyUniqueID);
			Guard.ArgumentNotNull(collectionDisplayName);

			using var generator = new UniqueIDGenerator();
			generator.Add(assemblyUniqueID);
			generator.Add(collectionDisplayName);
			generator.Add(collectionDefinitionClassName ?? string.Empty);
			return generator.Compute();
		}

		/// <summary>
		/// Computes a unique ID for a test method, to be placed into <see cref="_TestMethodMessage.TestMethodUniqueID"/>
		/// </summary>
		/// <param name="testClassUniqueID">The unique ID of the parent test class for the test method</param>
		/// <param name="methodName">The optional test method name</param>
		/// <returns>The computed unique ID for the test method (may return <c>null</c> if either the class
		/// unique ID or the method name is null)</returns>
		[return: NotNullIfNotNull("methodName")]
		public static string? ForTestMethod(
			string? testClassUniqueID,
			string? methodName)
		{
			if (testClassUniqueID == null || methodName == null)
				return null;

			using var generator = new UniqueIDGenerator();
			generator.Add(testClassUniqueID);
			generator.Add(methodName);
			return generator.Compute();
		}

		static char ToHexChar(int b) =>
			(char)(b < 10 ? b + '0' : b - 10 + 'a');

		static string ToHexString(byte[] bytes)
		{
			var chars = new char[bytes.Length * 2];
			var idx = 0;

			foreach (var @byte in bytes)
			{
				chars[idx++] = ToHexChar(@byte >> 4);
				chars[idx++] = ToHexChar(@byte & 0xF);
			}

			return new string(chars);
		}

		/// <summary>
		/// Add test method arguments.
		/// </summary>
		void AddObjectValue(object? obj)
		{
			if (ReferenceEquals(obj, null))
			{
				Add("null");
				return;
			}
			var objType = obj.GetType();
			var objTypeInfo = Reflector.Wrap(objType);
			if (obj is IEnumerable list)
			{
				foreach (var item in list)
				{
					AddObjectValue(item);
				}
			}
			if (objType == typeof(string))
			{
				Add((string)obj);
			}
			Add(TypeUtility.ConvertToSimpleTypeName(objTypeInfo));
			if (objType == typeof(Enum))
			{
				// Convert to int
				AddObjectValue((int)obj);
				return;
			}
			if (primitives.TryGetValue(objType, out Func<object, byte[]>? operation))
			{
				byte[] arr = operation(obj);
				AddByteArray(arr);
				return;
			}
			// Still here: assume a complex type.
			var fields = objType.GetFields();
			foreach (var field in fields)
			{
				if (!field.IsStatic)
				{
					Add("Field:");
					Add(field.Name);
					AddObjectValue(field.GetValue(obj));
				}
			}
		}

		static void AddUintObject(UniqueIDGenerator generator, object obj)
		{
			var bytes = BitConverter.GetBytes((uint)obj);
			lock (generator.stream)
			{
				generator.stream.Write(bytes, 0, bytes.Length);
			}
		}

		static void AddIntObject(UniqueIDGenerator generator, object obj)
		{
			var bytes = BitConverter.GetBytes((int)obj);
			lock (generator.stream)
			{
				generator.stream.Write(bytes, 0, bytes.Length);
			}
		}

		static void AddStringObject(UniqueIDGenerator generator, object obj)
		{
			generator.Add((string)obj);
		}
	}
}
