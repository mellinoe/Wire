﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Wire
{
    public class Serializer
    {
        private readonly Dictionary<Type, ValueSerializer> _serializers = new Dictionary<Type, ValueSerializer>();

        public ValueSerializer GetSerializerByType(Type type)
        {
            if (type == typeof(int))
                return Int32Serializer.Instance;

            if (type == typeof(long))
                return Int64Serializer.Instance;

            if (type == typeof(short))
                return Int16Serializer.Instance;

            if (type == typeof(byte))
                return ByteSerializer.Instance;

            if (type == typeof(bool))
                return BoolSerializer.Instance;

            if (type == typeof(DateTime))
                return DateTimeSerializer.Instance;

            if (type == typeof(string))
                return StringSerializer.Instance;

            if (type == typeof(byte[]))
                return ByteArraySerializer.Instance;

            if (type.IsArray &&
                (type == typeof(int[]) ||
                type == typeof(long[]) ||
                type == typeof(short[]) ||
                type == typeof(DateTime[]) ||
                type == typeof(bool[]) ||
                type == typeof(string[])
                ))
            {
                return ConsistentArraySerializer.Instance;
            }

            var serializer = GetSerialzerForPoco(type);

            return serializer;
        }

        private ValueSerializer GetSerialzerForPoco(Type type)
        {
            ValueSerializer serializer;
            if (!_serializers.TryGetValue(type, out serializer))
            {
                serializer = BuildSerializer(type);
                _serializers.Add(type, serializer);
            }
            return serializer;
        }

        private ValueSerializer BuildSerializer(Type type)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var serializer = new ObjectSerializer();

            var fieldWriters = new List<Action<Stream, object, SerializerSession>>();
            var fieldReaders = new List<Action<Stream, object, SerializerSession>>();
            foreach (var field in fields)
            {
                var f = field;
                var s = GetSerializerByType(field.FieldType);

                Func<object, object> getFieldValue = GenerateFieldReader(type, f);

                Action<Stream, object, SerializerSession> fieldWriter = (stream, o, session) =>
                {
                    var value = getFieldValue(o);
                    s.WriteValue(stream, value, session);
                };
                fieldWriters.Add(fieldWriter);

                Action<Stream, object, SerializerSession> fieldReader = (stream, o, session) =>
                {

                    var value = s.ReadValue(stream, session);
                    f.SetValue(o, value);
                };
                fieldReaders.Add(fieldReader);
            }

            serializer.Writer = (stream, o, session) =>
            {
                for (int index = 0; index < fieldWriters.Count; index++)
                {
                    var fieldWriter = fieldWriters[index];
                    fieldWriter(stream, o, session);
                }
            };
            serializer.Reader = (stream, session) =>
            {
                var instance = Activator.CreateInstance(type);
                for (int index = 0; index < fieldReaders.Count; index++)
                {
                    var fieldReader = fieldReaders[index];
                    fieldReader(stream, instance, session);
                }
                return instance;
            };
            return serializer;
        }

        private static Func<object, object> GenerateFieldReader(Type type, FieldInfo f)
        {
            ParameterExpression param = Expression.Parameter(typeof(object));
            Expression castParam = Expression.Convert(param, type);
            Expression x = Expression.Field(castParam, f);
            Expression castRes = Expression.Convert(x, typeof(object));
            Func<object, object> getFieldValue = Expression.Lambda<Func<object, object>>(castRes, param).Compile();
            return getFieldValue;
        }

        public void Serialize(object obj, Stream stream)
        {
            var session = new SerializerSession()
            {
                Buffer = new byte[100],
                Serializer = this,
            };
            var type = obj.GetType();
            var s = GetSerializerByType(obj.GetType());
            s.WriteManifest(stream, type, session);
            s.WriteValue(stream, obj, session);
        }

        public T Deserialize<T>(Stream stream)
        {
            var session = new SerializerSession()
            {
                Buffer = new byte[100],
                Serializer = this,
            };
            var s = GetSerializerByManifest(stream, session);
            return (T)s.ReadValue(stream, session);
        }

        public ValueSerializer GetSerializerByManifest(Stream stream, SerializerSession session)
        {
            var first = stream.ReadByte();
            switch (first)
            {
                case 2:
                    return Int64Serializer.Instance;
                case 3:
                    return Int16Serializer.Instance;
                case 4:
                    return ByteSerializer.Instance;
                case 5:
                    return DateTimeSerializer.Instance;
                case 6:
                    return BoolSerializer.Instance;
                case 7:
                    return StringSerializer.Instance;
                case 8:
                    return Int32Serializer.Instance;
                case 9:
                    return ByteArraySerializer.Instance;
                case 10:
                    return ConsistentArraySerializer.Instance;
                case 255:
                    Type type = GetTypeFromManifest(stream, session);
                    return GetSerialzerForPoco(type);
                default:
                    throw new NotSupportedException("Unknown manifest value");
            }
        }

        public Type GetArrayElementTypeFromManifest(Stream stream, SerializerSession session)
        {
            var first = stream.ReadByte();
            switch (first)
            {
                case 2:
                    return typeof(long);
                case 3:
                    return typeof(short);
                case 4:
                    return typeof(byte);
                case 5:
                    return typeof(DateTime);
                case 6:
                    return typeof(bool);
                case 7:
                    return typeof(string);
                case 8:
                    return typeof(int);
                case 9:
                    return typeof(byte[]);
                case 10:
                    throw new NotSupportedException(); //
                case 255:
                    Type type = GetTypeFromManifest(stream, session);
                    return type;
                default:
                    throw new NotSupportedException("Unknown manifest value");
            }
        }

        public  Type GetTypeFromManifest(Stream stream, SerializerSession session)
        {
            var bytes = (byte[])ByteArraySerializer.Instance.ReadValue(stream, session);
            var typename = Encoding.UTF8.GetString(bytes);
            var type = Type.GetType(typename);
            return type;
        }
    }
}