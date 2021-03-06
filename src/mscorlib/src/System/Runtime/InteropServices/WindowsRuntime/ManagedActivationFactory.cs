// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

//

using System;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Runtime.CompilerServices;

namespace System.Runtime.InteropServices.WindowsRuntime
{
    [ComImport]
    [Guid("60D27C8D-5F61-4CCE-B751-690FAE66AA53")]
    [WindowsRuntimeImport]
    internal interface IManagedActivationFactory
    {
        void RunClassConstructor();
    }

    // A ManangedActivationFactory provides the IActivationFactory implementation for managed types which are
    // constructable via Windows Runtime. Implementation of specialized factory and static WinRT interfaces is
    // provided using VM functionality (see Marshal.InitializeWinRTFactoryObject for details).
    //
    // In order to be activatable via the ManagedActivationFactory type, the type must be decorated with either
    // ActivatableAttribute, or StaticAttribute.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    internal sealed class ManagedActivationFactory : IActivationFactory, IManagedActivationFactory
    {
        private Type m_type;

        [SecurityCritical]
        internal ManagedActivationFactory(Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            // Check whether the type is "exported to WinRT", i.e. it is declared in a managed .winmd and is decorated
            // with at least one ActivatableAttribute or StaticAttribute.
            if (!(type is RuntimeType) || !type.IsExportedToWindowsRuntime)
                throw new ArgumentException(Environment.GetResourceString("Argument_TypeNotActivatableViaWindowsRuntime", type), "type");
            
            m_type = type;
        }

        // Activate an instance of the managed type by using its default constructor.
        public object ActivateInstance()
        {
            try
            {
                return Activator.CreateInstance(m_type);
            }
            catch (MissingMethodException)
            {
                // If the type doesn't expose a default constructor, then we fail with E_NOTIMPL
                throw new NotImplementedException();
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException;
            }
        }

        // Runs the class constructor
        // Currently only Jupiter use this to run class constructor in order to 
        // initialize DependencyProperty objects and do necessary work
        void IManagedActivationFactory.RunClassConstructor()
        {
            RuntimeHelpers.RunClassConstructor(m_type.TypeHandle);
        }
    }
}
