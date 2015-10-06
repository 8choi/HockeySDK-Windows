﻿namespace Microsoft.ApplicationInsights.Extensibility.Implementation
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Net;
    using System.Reflection;
    using System.Xml.Linq;
    using Microsoft.ApplicationInsights.Channel;
    using Microsoft.ApplicationInsights.DataContracts;
    using Microsoft.ApplicationInsights.Extensibility;
    using Microsoft.ApplicationInsights.Extensibility.Implementation.Platform;
    using Microsoft.ApplicationInsights.TestFramework;

#if WINDOWS_PHONE || WINDOWS_STORE
    using Microsoft.VisualStudio.TestPlatform.UnitTestFramework;
#else
    using Microsoft.VisualStudio.TestTools.UnitTesting;
#endif
    using Assert = Xunit.Assert;

    [TestClass]
    public class TelemetryConfigurationFactoryTest
    {
        #region Instance

        [TestMethod]
        public void ClassIsInternalAndNotMeantForPublicConsumption()
        {
            Assert.False(typeof(TelemetryConfigurationFactory).GetTypeInfo().IsPublic);
        }

        [TestMethod]
        public void InstanceReturnsDefaultTelemetryConfigurationFactoryInstanceUsedByTelemetryConfiguration()
        {
            Assert.NotNull(TelemetryConfigurationFactory.Instance);
        }

        [TestMethod]
        public void InstanceCanGeSetByTestsToIsolateTestingOfTelemetryConfigurationFromRealFactoryLogic()
        {
            var replacement = new TestableTelemetryConfigurationFactory();
            TelemetryConfigurationFactory.Instance = replacement;
            Assert.Same(replacement, TelemetryConfigurationFactory.Instance);
        }

        [TestMethod]
        public void InstanceIsLazilyInitializedToSimplifyResettingOfGlobalStateInTests()
        {
            TelemetryConfigurationFactory.Instance = null;
            Assert.NotNull(TelemetryConfigurationFactory.Instance);
        }

        #endregion

        #region Initialize

        [TestMethod]
        public void InitializeCreatesInMemoryChannel()
        {
            var configuration = new TelemetryConfiguration();
            new TestableTelemetryConfigurationFactory().Initialize(configuration);

            Assert.IsType<InMemoryChannel>(configuration.TelemetryChannel);
        }

        [TestMethod]
        public void InitializeCreatesTimestampPropertyInitializerByDefaultBecauseItIsNeededOnAllPlatforms()
        {
            var configuration = new TelemetryConfiguration();
            new TestableTelemetryConfigurationFactory().Initialize(configuration);
            Assert.Equal(1, configuration.TelemetryInitializers.Count(i => i is TimestampPropertyInitializer));
        }

        [TestMethod]
        public void InitializesInstanceWithInformationFromConfigurationFileWhenItExists()
        {
            string configFileContents = Configuration("<InstrumentationKey>F8474271-D231-45B6-8DD4-D344C309AE69</InstrumentationKey>");
            var platform = new StubPlatform { OnReadConfigurationXml = () => configFileContents };
            PlatformSingleton.Current = platform;
            try
            {
                var configuration = new TelemetryConfiguration();
                new TestableTelemetryConfigurationFactory().Initialize(configuration);

                // Assume that LoadFromXml method is called, tested separately
                Assert.False(string.IsNullOrEmpty(configuration.InstrumentationKey));
            }
            finally
            {
                PlatformSingleton.Current = null;
            }
        }

        [TestMethod]
        public void InitializeAddsSdkVersionContextInitializerByDefault()
        {
            var configuration = new TelemetryConfiguration();
            new TestableTelemetryConfigurationFactory().Initialize(configuration);

            // Assume that SdkVersionInitializer is added by default
            var contextInitializer = configuration.ContextInitializers[0];
            Assert.IsType<SdkVersionPropertyContextInitializer>(contextInitializer);
        }
        
        [TestMethod]
        public void InitializeNotifiesTelemetryInitializersImplementingITelemetryModuleInterface()
        {
            var initializer = new StubConfigurableTelemetryInitializer();
            var configuration = new TelemetryConfiguration { TelemetryInitializers = { initializer } };

            new TestableTelemetryConfigurationFactory().Initialize(configuration);

            Assert.True(initializer.Initialized);
            Assert.Same(configuration, initializer.Configuration);
        }

        [TestMethod]
        public void InitializeNotifiesContextInitializersImplementingITelemetryModuleInterface()
        {
            var initializer = new StubConfigurableContextInitializer();
            var configuration = new TelemetryConfiguration { ContextInitializers = { initializer } };

            new TestableTelemetryConfigurationFactory().Initialize(configuration);

            Assert.True(initializer.Initialized);
            Assert.Same(configuration, initializer.Configuration);
        }

        #endregion

        #region CreateInstance

        [TestMethod]
        public void CreateInstanceReturnsInstanceOfTypeSpecifiedByTypeName()
        {
            var configuration = new TelemetryConfiguration();
            Type type = typeof(StubTelemetryInitializer);
            object instance = TestableTelemetryConfigurationFactory.CreateInstance(typeof(ITelemetryInitializer), type.AssemblyQualifiedName);
            Assert.Equal(type, instance.GetType());
        }

        [TestMethod]
        public void CreateInstanceThrowsInvalidOperationExceptionWhenTypeCannotBeFoundToHelpDeveloperIdentifyAndFixTheProblem()
        {
            var configuration = new TelemetryConfiguration();
            var exception = Assert.Throws<InvalidOperationException>(
                () => TestableTelemetryConfigurationFactory.CreateInstance(typeof(ITelemetryInitializer), "MissingType, MissingAssembly"));
            Assert.Contains("MissingType", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void CreateInstanceThrowsInvalidOperationExceptionWhenTypeNameIsInvalidToHelpDeveloperIdentifyAndFixTheProblem()
        {
            var configuration = new TelemetryConfiguration();
            var exception = Assert.Throws<InvalidOperationException>(
                () => TestableTelemetryConfigurationFactory.CreateInstance(typeof(ITelemetryInitializer), "Invalid Type Name"));
            Assert.Contains("Invalid Type Name", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void CreateInstanceThrowsInvalidOperationExceptionWhenInstanceDoesNotImplementExpectedInterfaceToHelpDeveloperIdentifyAndFixTheProblem()
        {
            var configuration = new TelemetryConfiguration();
            Type invalidType = typeof(object);
            var exception = Assert.Throws<InvalidOperationException>(
                () => TestableTelemetryConfigurationFactory.CreateInstance(typeof(ITelemetryInitializer), invalidType.AssemblyQualifiedName));
            Assert.Contains(invalidType.AssemblyQualifiedName, exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(typeof(ITelemetryInitializer).Name, exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region LoadFromXml

        [TestMethod]
        public void LoadFromXmlInitializesGivenTelemetryConfigurationInstanceFromXml()
        {
            string expected = Guid.NewGuid().ToString();
            string profile = Configuration("<InstrumentationKey>" + expected + "</InstrumentationKey>");

            var configuration = new TelemetryConfiguration();
            TestableTelemetryConfigurationFactory.LoadFromXml(configuration, XDocument.Parse(profile));

            // Assume LoadFromXml calls LoadInstance, which is tested separately.
            Assert.Equal(expected, configuration.InstrumentationKey);
        }

        #endregion

        #region LoadInstance

        [TestMethod]
        public void LoadInstanceReturnsInstanceOfTypeSpecifiedInTypeAttributeOfGivenXmlDefinition()
        {
            var definition = new XElement("Definition", new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName));
            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null);
            Assert.Equal(typeof(StubClassWithProperties), instance.GetType());
        }

        [TestMethod]
        public void LoadInstanceSetsInstancePropertiesFromChildElementValuesOfDefinition()
        {
            var definition = new XElement(
                "Definition",
                new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName),
                new XElement("StringProperty", "TestValue"));

            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null);

            Assert.Equal("TestValue", ((StubClassWithProperties)instance).StringProperty);
        }

        [TestMethod]
        public void LoadInstanceSetsInstancePropertiesOfTimeSpanTypeFromChildElementValuesOfDefinitionWithTimeSpanFormat()
        {
            var definition = new XElement(
                "Definition",
                new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName),
                new XElement("TimeSpanProperty", "00:00:07"));

            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null);

            Assert.Equal(TimeSpan.FromSeconds(7), ((StubClassWithProperties)instance).TimeSpanProperty);
        }

        [TestMethod]
        public void LoadInstanceSetsInstancePropertiesOfTimeSpanTypeFromChildElementValuesOfDefinitionWithOneInteger()
        {
            var definition = new XElement(
                "Definition",
                new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName),
                new XElement("TimeSpanProperty", "7"));

            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null);

            Assert.Equal(TimeSpan.FromDays(7), ((StubClassWithProperties)instance).TimeSpanProperty);
        }

        [TestMethod]
        public void LoadInstanceSetsInstancePropertiesOfTimeSpanTypeFromChildElementValuesOfDefinitionWithInvalidFormatThrowsException()
        {
            var definition = new XElement(
                "Definition",
                new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName),
                new XElement("TimeSpanProperty", "TestValue"));

            Assert.Throws<FormatException>(() => TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null));
        }

        [TestMethod]
        public void LoadInstanceInitializesGivenInstanceAndDoesNotRequireSpecifyingTypeAttributeToSimplifyConfiguration()
        {
            var definition = new XElement(
                "Definition",
                new XElement("StringProperty", "TestValue"));

            var original = new StubClassWithProperties();
            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), original);

            Assert.Equal("TestValue", original.StringProperty);
        }

        [TestMethod]
        public void LoadInstanceConvertsValueToExpectedTypeGivenXmlDefinitionWithNoChildElements()
        {
            var definition = new XElement("Definition", "42");
            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(int), null);
            Assert.Equal(42, instance);
        }

        [TestMethod]
        public void LoadInstanceTrimsValueOfGivenXmlElementToIgnoreWhitespaceUsersMayAddToConfiguration()
        {
            string expected = Guid.NewGuid().ToString();
            var definition = new XElement("InstrumentationKey", "\n" + expected + "\n");

            object actual = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(string), null);

            Assert.Equal(expected, actual);
        }

        [TestMethod]
        public void LoadInstanceReturnsNullGivenEmptyXmlElementForReferenceType()
        {
            var definition = new XElement("Definition");
            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(string), "Test Value");
            Assert.Null(instance);
        }

        [TestMethod]
        public void LoadInstanceReturnsOriginalValueGivenNullXmlElement()
        {
            var original = "Test Value";
            object loaded = TestableTelemetryConfigurationFactory.LoadInstance(null, original.GetType(), original);
            Assert.Same(original, loaded);
        }

        [TestMethod]
        public void LoadInstanceReturnsDefaultValueGivenValueEmptyXmlElementForValueType()
        {
            var definition = new XElement("Definition");
            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(int), 12);
            Assert.Equal(0, instance);
        }

        [TestMethod]
        public void LoadInstanceThrowsInvalidOperationExceptionWhenDefinitionElementDoesNotHaveTypeAttributeAndInstanceIsNotInitialized()
        {
            var elementWithoutType = new XElement("Add", new XElement("PropertyName"));
            var exception = Assert.Throws<InvalidOperationException>(() => TestableTelemetryConfigurationFactory.LoadInstance(elementWithoutType, typeof(IComparable), null));
            Assert.Contains(elementWithoutType.Name.ToString(), exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Type", exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void LoadInstanceThrowsInvalidOperationExceptionWhenDefinitionElementContainsInvalidContentAndNoTypeAttribute()
        {
            var definition = new XElement("InvalidElement", "InvalidText");
            var exception = Assert.Throws<InvalidOperationException>(
                () => TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(ITelemetryChannel), null));
            Assert.Contains("InvalidElement", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("InvalidText", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(exception.InnerException);
        }

        [TestMethod]
        public void LoadInstanceCreatesNewInstanceOfExpectedTypeWhenTypeAttributeIsNotSpecified()
        {
            var definition = new XElement("Definition", new XElement("Int32Property", 42));

            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null);

            var loaded = Assert.IsType<StubClassWithProperties>(instance);
            Assert.Equal(42, loaded.Int32Property);
        }

        [TestMethod]
        public void LoadInstanceCreatesNewInstanceOfExpectedTypeWhenPropertiesAreSpecifiedOnlyAsAttributes()
        {
            var definition = new XElement("Definition", new XAttribute("Int32Property", 42));

            object instance = TestableTelemetryConfigurationFactory.LoadInstance(definition, typeof(StubClassWithProperties), null);

            var loaded = Assert.IsType<StubClassWithProperties>(instance);
            Assert.Equal(42, loaded.Int32Property);
        }

        #endregion

        #region LoadInstances<T>

        [TestMethod]
        public void LoadInstancesPopulatesListWithInstancesOfSpecifiedType()
        {
            var element = XElement.Parse(@"
                <List xmlns=""http://schemas.microsoft.com/ApplicationInsights/2013/Settings"">
                    <Add Type=""" + typeof(StubTelemetryInitializer).AssemblyQualifiedName + @""" />           
                </List>");
            var instances = new List<ITelemetryInitializer>();

            TestableTelemetryConfigurationFactory.LoadInstances(element, instances);

            Assert.Equal(1, instances.Count);
            Assert.Equal(typeof(StubTelemetryInitializer), instances[0].GetType());
        }

        [TestMethod]
        public void LoadInstancesUpdatesInstanceWithMatchingType() // TODO: Why? This is inconsistent with the name of the element, Add.
        {
            var configuration = new TelemetryConfiguration();
            var element = XElement.Parse(@"
                <List xmlns=""http://schemas.microsoft.com/ApplicationInsights/2013/Settings"">
                    <Add Type=""" + typeof(StubConfigurableWithProperties).AssemblyQualifiedName + @""" > 
                        <Int32Property>77</Int32Property>
                    </Add>
                </List>");

            var configurableElement = new StubConfigurableWithProperties(configuration);
            var instances = new List<object>();
            instances.Add(configurableElement);

            TestableTelemetryConfigurationFactory.LoadInstances(element, instances);

            var telemetryModules = instances.OfType<StubConfigurableWithProperties>().ToArray();
            Assert.Equal(1, telemetryModules.Count());
            Assert.Equal(configurableElement, telemetryModules[0]);
            Assert.Equal(77, configurableElement.Int32Property);
        }

        [TestMethod]
        public void LoadInstancesPopulatesListWithPrimitiveValues()
        {
            var definition = XElement.Parse(@"
                <List xmlns=""http://schemas.microsoft.com/ApplicationInsights/2013/Settings"">
                    <Add>41</Add>
                    <Add>42</Add>
                </List>");

            var instances = new List<int>();
            TestableTelemetryConfigurationFactory.LoadInstances(definition, instances);

            Assert.Equal(new[] { 41, 42 }, instances);
        }

        [TestMethod]
        public void LoadInstancesIgnoresElementsOtherThanAdd() // TODO: Why? This is inconsistent with property loading, which throws InvalidOperationException.
        {
            var definition = XElement.Parse(@"
                <List xmlns=""http://schemas.microsoft.com/ApplicationInsights/2013/Settings"">
                    <Unknown/>
                    <Add>42</Add>
                </List>");

            var instances = new List<int>();
            Assert.DoesNotThrow(() => TestableTelemetryConfigurationFactory.LoadInstances(definition, instances));

            Assert.Equal(new[] { 42 }, instances);
        }

        #endregion

        #region LoadProperties

        [TestMethod]
        public void LoadPropertiesConvertsPropertyValuesFromStringToPropertyType()
        {
            var definition = new XElement("Definition", new XElement("Int32Property", "42"));

            var instance = new StubClassWithProperties();
            TestableTelemetryConfigurationFactory.LoadProperties(definition, instance);

            Assert.Equal(42, instance.Int32Property);
        }

        [TestMethod]
        public void LoadPropertiesThrowsInvalidOperationExceptionWhenInstanceDoesNotHavePropertyWithSpecifiedName()
        {
            var definition = new XElement("Definition", new XElement("InvalidProperty", "AnyValue"));
            var exception = Assert.Throws<InvalidOperationException>(
                () => TestableTelemetryConfigurationFactory.LoadProperties(definition, new StubClassWithProperties()));
            Assert.Contains("InvalidProperty", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(typeof(StubClassWithProperties).AssemblyQualifiedName, exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        [TestMethod]
        public void LoadPropertiesIgnoresUnknownTelemetryConfigurationPropertiesToAllowStatusMonitorDefineItsOwnSections()
        {
            string configuration = Configuration("<UnknownSection/>");
            XElement aplicationInsightsElement = XDocument.Parse(configuration).Root;
            Assert.DoesNotThrow(() => TestableTelemetryConfigurationFactory.LoadProperties(aplicationInsightsElement, new TelemetryConfiguration()));
        }

        [TestMethod]
        public void LoadPropertiesInstantiatesObjectOfTypeSpecifiedInTypeAttribute()
        {
            var definition = new XElement("Definition", new XElement("ChildProperty", new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName)));
            var instance = new StubClassWithProperties();

            TestableTelemetryConfigurationFactory.LoadProperties(definition, instance);

            Assert.Equal(typeof(StubClassWithProperties), instance.ChildProperty.GetType());
        }

        [TestMethod]
        public void LoadPropertiesRecursivelyLoadsInstanceSpecifiedByTypeAttribute()
        {
            var definition = new XElement(
                "Definition",
                new XElement(
                    "ChildProperty",
                    new XAttribute("Type", typeof(StubClassWithProperties).AssemblyQualifiedName),
                    new XElement("StringProperty", "TestValue")));
            var instance = new StubClassWithProperties();

            TestableTelemetryConfigurationFactory.LoadProperties(definition, instance);

            Assert.Equal("TestValue", instance.ChildProperty.StringProperty);
        }

        [TestMethod]
        public void LoadPropertiesDoesNotAttemptToSetReadOnlyProperty()
        {
            XElement definition = XDocument.Parse(Configuration(@"<TelemetryModules/>")).Root;
            var instance = new TelemetryConfiguration();
            Assert.DoesNotThrow(() => TestableTelemetryConfigurationFactory.LoadProperties(definition, instance));
        }

        [TestMethod]
        public void LoadPropertiesLoadsPropertiesFromAttributes()
        {
            var definition = new XElement("Definition", new XAttribute("Int32Property", "42"));

            var instance = new StubClassWithProperties();
            TestableTelemetryConfigurationFactory.LoadProperties(definition, instance);

            Assert.Equal(42, instance.Int32Property);
        }

        [TestMethod]
        public void LoadPropertiesGivesPrecedenceToValuesFromElementsBecauseTheyAppearBelowAttributes()
        {
            var definition = new XElement("Definition", new XAttribute("Int32Property", "41"), new XElement("Int32Property", "42"));

            var instance = new StubClassWithProperties();
            TestableTelemetryConfigurationFactory.LoadProperties(definition, instance);

            Assert.Equal(42, instance.Int32Property);
        }

        [TestMethod]
        public void LoadPropertiesIgnoresNamespaceDeclarationWhenLoadingFromAttributes()
        {
            var definition = new XElement("Definition", new XAttribute("xmlns", "http://somenamespace"));

            var instance = new StubClassWithProperties();

            Assert.DoesNotThrow(() => TestableTelemetryConfigurationFactory.LoadProperties(definition, instance));
        }

        [TestMethod]
        public void DeveloperModePropertyCanLoadTrueValue()
        {
            TelemetryConfiguration instance = CreateTelemetryConfigurationWithDeveloperModeValue("true");
            Assert.True(instance.TelemetryChannel.DeveloperMode.HasValue);
            Assert.True(instance.TelemetryChannel.DeveloperMode.Value);
        }

        [TestMethod]
        public void DeveloperModePropertyCanLoadFalseValue()
        {
            TelemetryConfiguration instance = CreateTelemetryConfigurationWithDeveloperModeValue("false");
            Assert.True(instance.TelemetryChannel.DeveloperMode.HasValue);
            Assert.False(instance.TelemetryChannel.DeveloperMode.Value);
        }

        [TestMethod]
        public void DeveloperModePropertyCanLoadNullValue()
        {
            TelemetryConfiguration instance = CreateTelemetryConfigurationWithDeveloperModeValue("null");
            Assert.False(instance.TelemetryChannel.DeveloperMode.HasValue);
        }

        [TestMethod]
        public void DeveloperModePropertyCanLoadEmptyValue()
        {
            TelemetryConfiguration instance = CreateTelemetryConfigurationWithDeveloperModeValue(string.Empty);
            Assert.False(instance.TelemetryChannel.DeveloperMode.HasValue);
        }

        #endregion

        private static TelemetryConfiguration CreateTelemetryConfigurationWithDeveloperModeValue(string developerModeValue)
        {
            XElement definition = XDocument.Parse(Configuration(
    @"<TelemetryChannel Type=""Microsoft.ApplicationInsights.TestFramework.StubTelemetryChannel, Microsoft.ApplicationInsights.TestFramework"">
                    <DeveloperMode>" + developerModeValue + @"</DeveloperMode>
                 </TelemetryChannel>")).Root;

            var instance = new TelemetryConfiguration();
            Assert.DoesNotThrow(() => TestableTelemetryConfigurationFactory.LoadProperties(definition, instance));
            return instance;
        }

        private static string Configuration(string innerXml)
        {
            return
              @"<?xml version=""1.0"" encoding=""utf-8"" ?>
                <ApplicationInsights xmlns=""http://schemas.microsoft.com/ApplicationInsights/2013/Settings"">
" + innerXml + @"
                </ApplicationInsights>";
        }

        private class TestableTelemetryConfigurationFactory : TelemetryConfigurationFactory
        {
            public static new object CreateInstance(Type interfaceType, string typeName)
            {
                return TelemetryConfigurationFactory.CreateInstance(interfaceType, typeName);
            }

            public static new void LoadFromXml(TelemetryConfiguration configuration, XDocument xml)
            {
                TelemetryConfigurationFactory.LoadFromXml(configuration, xml);
            }

            public static new object LoadInstance(XElement definition, Type expectedType, object instance)
            {
                return TelemetryConfigurationFactory.LoadInstance(definition, expectedType, instance);
            }

            [SuppressMessage("Microsoft.Design", "CA1061:DoNotHideBaseClassMethods", Justification = "This method allows calling protected base method in this test class.")]
            public static new void LoadInstances<T>(XElement definition, ICollection<T> instances)
            {
                TelemetryConfigurationFactory.LoadInstances(definition, instances);
            }

            public static new void LoadProperties(XElement definition, object instance)
            {
                TelemetryConfigurationFactory.LoadProperties(definition, instance);
            }
        }

        private class StubClassWithProperties
        {
            public int Int32Property { get; set; }

            public string StringProperty { get; set; }

            public TimeSpan TimeSpanProperty { get; set; }

            public StubClassWithProperties ChildProperty { get; set; }
        }

        private class StubConfigurable : ITelemetryModule
        {
            public StubConfigurable()
            {
            }

            public TelemetryConfiguration Configuration { get; set; }

            public bool Initialized { get; set; }

            public void Initialize(TelemetryConfiguration configuration)
            {
                this.Configuration = configuration;
                this.Initialized = true;
            }
        }

        private class StubConfigurableContextInitializer : StubConfigurable, IContextInitializer
        {
            public void Initialize(TelemetryContext context)
            {
            }
        }

        private class StubConfigurableTelemetryInitializer : StubConfigurable, ITelemetryInitializer
        {
            public void Initialize(ITelemetry telemetry)
            {
            }
        }

        private class StubConfigurableWithProperties : ITelemetryModule
        {
            public StubConfigurableWithProperties(TelemetryConfiguration configuration)
            {
                this.Configuration = configuration;
            }

            public int Int32Property { get; set; }

            public string StringProperty { get; set; }

            public TelemetryConfiguration Configuration { get; set; }

            public Action<TelemetryConfiguration> OnInitialize { get; set; }

            public void Initialize(TelemetryConfiguration configuration)
            {
                if (this.OnInitialize != null)
                {
                    this.OnInitialize(configuration);
                }
            }
        }
    }
}
