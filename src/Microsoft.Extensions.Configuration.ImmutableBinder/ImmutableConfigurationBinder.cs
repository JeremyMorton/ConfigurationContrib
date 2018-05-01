using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Microsoft.Extensions.Configuration.ImmutableBinder
{
    /// <summary>
    ///     Static helper class that allows binding strongly immutable typed objects to configuration values.
    /// </summary>
    public static class ImmutableConfigurationBinder
    {
        /// <summary>
        ///     Attempts to bind the configuration instance to a new instance of type <typeparamref name="T"/>.
        ///     If this configuration section has a value, that will be used.
        ///     Otherwise binding by matching constructor arguments against configuration keys recursively.
        /// </summary>
        /// <typeparam name="T">
        ///     The type of the new instance to bind.
        /// </typeparam>
        /// <param name="configuration">
        ///     The configuration instance to bind.
        /// </param>
        /// <returns>
        ///     The new instance of <typeparamref name="T"/>.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        ///     If <paramref name="configuration"/> is <see langword="null" />.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        ///     If binding of the <paramref name="configuration"/> failed due to any reason.
        /// </exception>
        public static T ImmutableBind<T>(this IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            T result = (T) BindType(typeof(T), configuration);
            return result;
        }

        private static object BindType(Type type, IConfiguration config)
        {
            var section = config as IConfigurationSection;
            var configValue = section?.Value;

            if (configValue != null &&
                type.TryConvertValue(configValue, out object convertedValue, out Exception error))
            {
                if (error != null)
                {
                    throw error;
                }

                return convertedValue;
            }

            var dictionaryInterface = type.FindOpenGenericInterface(typeof(IReadOnlyDictionary<,>));
            if (dictionaryInterface != null)
            {
                return BindDictionary(type, config);
            }

            var collectionInterface = type.FindOpenGenericInterface(typeof(IReadOnlyCollection<>))
                                      ?? type.FindOpenGenericInterface(typeof(IReadOnlyList<>));
            if (collectionInterface != null)
            {
                return BindCollection(type, config);
            }

            // check for a default value

            var constructors = type.GetConstructors();

            // For the first version, just take the first constructor.
            var constructor = constructors.FirstOrDefault();
            if (constructor == null)
            {
                throw new InvalidOperationException("No constructor found for " + type);
            }

            var values = new List<object>();
            foreach (var parameter in constructor.GetParameters())
            {
                object value;
                try
                {
                    value = BindType(parameter.ParameterType, config.GetSection(parameter.Name));
                }
                catch (Exception e) when (parameter.HasDefaultValue &&
                                          (e is InvalidOperationException || e is IndexOutOfRangeException))
                {
                    // Fall back to default value, if specified.
                    value = parameter.DefaultValue;
                }

                values.Add(value);
            }

            var instance = constructor.Invoke(values.ToArray());

            return instance;
        }

        private static object BindCollection(Type type, IConfiguration config)
        {
            var elementType = type.GetGenericArguments()[0];
            var collectionType = typeof(Collection<>).MakeGenericType(elementType);
            var collectionAddMethod = collectionType.GetMethod("Add");

            var collectionInstance = Activator.CreateInstance(collectionType);
            foreach (var section in config.GetChildren())
            {
                var element = BindType(elementType, section);
                collectionAddMethod.Invoke(collectionInstance, new[] {element});
            }

            var readOnlyCollectionType = typeof(ReadOnlyCollection<>).MakeGenericType(elementType);
            var readOnlyCollectionInstance = Activator.CreateInstance(readOnlyCollectionType, collectionInstance);

            return readOnlyCollectionInstance;
        }

        private static object BindDictionary(Type type, IConfiguration config)
        {
            var keyType = type.GetGenericArguments()[0];
            var elementType = type.GetGenericArguments()[1];
            var dictionaryType = typeof(Dictionary<,>).MakeGenericType(keyType, elementType);
            var dictionaryAddMethod = dictionaryType.GetMethod("Add");

            var dictionaryInstance = Activator.CreateInstance(dictionaryType);
            foreach (var section in config.GetChildren())
            {
                if (!keyType.TryConvertValue(section.Key, out object convertedKeyValue, out Exception error))
                {
                    throw error;
                }

                var element = BindType(elementType, section);
                dictionaryAddMethod.Invoke(dictionaryInstance, new[] { convertedKeyValue, element });
            }

            var readOnlyDictionaryType = typeof(ReadOnlyDictionary<,>).MakeGenericType(keyType, elementType);
            var readOnlyDictionaryInstance = Activator.CreateInstance(readOnlyDictionaryType, dictionaryInstance);

            return readOnlyDictionaryInstance;
        }
    }
}