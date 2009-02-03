﻿using System;
using System.Collections.Generic;
using System.Linq;
using Ninject.Activation;
using Ninject.Activation.Providers;
using Ninject.Activation.Scope;
using Ninject.Components;
using Ninject.Infrastructure;
using Ninject.Infrastructure.Disposal;
using Ninject.Modules;
using Ninject.Parameters;
using Ninject.Planning.Bindings;
using Ninject.Resolution;
using Ninject.Syntax;

namespace Ninject
{
	public abstract class KernelBase : DisposableObject, IKernel
	{
		private readonly Multimap<Type, IBinding> _bindings = new Multimap<Type, IBinding>();
		private readonly Dictionary<string, IModule> _modules = new Dictionary<string, IModule>();

		public INinjectSettings Settings { get; private set; }
		public IComponentContainer Components { get; private set; }

		protected KernelBase(params IModule[] modules)
			: this(new ComponentContainer(), new NinjectSettings(), modules) { }

		protected KernelBase(INinjectSettings settings, params IModule[] modules)
			: this(new ComponentContainer(), settings, modules) { }

		protected KernelBase(IComponentContainer components, INinjectSettings settings, params IModule[] modules)
		{
			Settings = settings;
			Components = components;
			components.Settings = settings;
			modules.Map(Load);
		}

		public override void Dispose()
		{
			if (Components != null) Components.Dispose();
			base.Dispose();
		}

		public virtual void Load(IModule module)
		{
			_modules.Add(module.Name, module);
			module.Kernel = this;
			module.Load();
		}

		public virtual void Unload(string moduleName)
		{
			IModule module = _modules[moduleName];

			module.Unload();
			module.Kernel = null;

			_modules.Remove(module.Name);
		}

		public virtual void Unload(IModule module)
		{
			Unload(module.Name);
		}

		public bool CanResolve(IRequest request)
		{
			var resolver = Components.Get<IResolver>();

			if (_bindings.ContainsKey(request.Service))
				return true;

			if (resolver.HasStrategy(request))
				return true;

			if (request.Service.IsGenericType && _bindings.ContainsKey(request.Service.GetGenericTypeDefinition()))
				return true;

			return false;
		}

		public virtual IEnumerable<IContext> Resolve(Type service, IEnumerable<IConstraint> constraints, IEnumerable<IParameter> parameters)
		{
			return Resolve(CreateDirectRequest(service, constraints, parameters));
		}

		public virtual IEnumerable<IContext> Resolve(IRequest request)
		{
			if (!CanResolve(request))
			{
				if (!TryRegisterImplicitSelfBinding(request.Service))
					throw new NotSupportedException(String.Format("No binding registered for {0}", request.Service)); 
			}

			return GetBindings(request)
				.Where(binding => binding.Matches(request) && request.ConstraintsSatisfiedBy(binding))
				.Select(binding => CreateContext(request, binding));
		}

		public IBindingToSyntax Bind<T>()
		{
			return Bind(typeof(T));
		}

		public IBindingToSyntax Bind(Type service)
		{
			var binding = new Binding(service);
			AddBinding(binding);
			return new BindingBuilder(binding);
		}

		public void AddBinding(IBinding binding)
		{
			_bindings.Add(binding.Service, binding);
		}

		public void RemoveBinding(IBinding binding)
		{
			_bindings.Remove(binding.Service, binding);
		}

		public virtual IEnumerable<IBinding> GetBindings(IRequest request)
		{
			foreach (IBinding binding in _bindings[request.Service])
				yield return binding;

			if (request.Service.IsGenericType)
			{
				Type gtd = request.Service.GetGenericTypeDefinition();

				foreach (IBinding binding in _bindings[gtd])
					yield return binding;
			}
		}

		public IResolutionRoot BeginScope()
		{
			return new ActivationScope(this);
		}

		public virtual bool TryRegisterImplicitSelfBinding(Type service)
		{
			if (service.IsInterface || service.IsAbstract || service.ContainsGenericParameters)
				return false;

			var binding = new Binding(service) { ProviderCallback = StandardProvider.GetCreationCallback(service) };
			AddBinding(binding);

			return true;
		}

		protected virtual IRequest CreateDirectRequest(Type service, IEnumerable<IConstraint> constraints, IEnumerable<IParameter> parameters)
		{
			return new Request(service, constraints, parameters, null);
		}

		protected virtual IContext CreateContext(IRequest request, IBinding binding)
		{
			return new Context(this, request, binding, Components.Get<IResolver>());
		}

		object IServiceProvider.GetService(Type serviceType)
		{
			return this.Get(serviceType);
		}
	}
}