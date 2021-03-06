﻿/*
 * Licensed to the Apache Software Foundation (ASF) under one
 * or more contributor license agreements.  See the NOTICE file
 * distributed with this work for additional information
 * regarding copyright ownership.  The ASF licenses this file
 * to you under the Apache License, Version 2.0 (the
 * "License"); you may not use this file except in compliance
 * with the License.  You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing,
 * software distributed under the License is distributed on an
 * "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
 * KIND, either express or implied.  See the License for the
 * specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Org.Apache.REEF.Common.Protobuf.ReefProtocol;
using Org.Apache.REEF.Common.Runtime.Evaluator;
using Org.Apache.REEF.Common.Runtime.Evaluator.Context;
using Org.Apache.REEF.Common.Runtime.Evaluator.Utils;
using Org.Apache.REEF.Common.Services;
using Org.Apache.REEF.Common.Tasks;
using Org.Apache.REEF.Driver.Bridge;
using Org.Apache.REEF.Evaluator.Exceptions;
using Org.Apache.REEF.Tang.Formats;
using Org.Apache.REEF.Tang.Implementations.InjectionPlan;
using Org.Apache.REEF.Tang.Implementations.Tang;
using Org.Apache.REEF.Tang.Interface;
using Org.Apache.REEF.Utilities;
using Org.Apache.REEF.Utilities.Logging;
using Org.Apache.REEF.Wake.Remote;
using Org.Apache.REEF.Wake.Remote.Impl;
using Org.Apache.REEF.Wake.Time.Runtime;
using Org.Apache.REEF.Wake.Time.Runtime.Event;

namespace Org.Apache.REEF.Evaluator
{
    public sealed class Evaluator
    {
        private static Logger logger = Logger.GetLogger(typeof(Evaluator));
        private static int heartbeatPeriodInMs = Constants.DefaultEvaluatorHeartbeatPeriodInMs;
        private static int heartbeatMaxRetry = Constants.DefaultEvaluatorHeartbeatMaxRetry;
        private static IInjector injector;
        private static EvaluatorConfigurations evaluatorConfig;

        public static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("START: {0} Evaluator::InitInjector.", DateTime.Now);
                Stopwatch timer = new Stopwatch();
                InitInjector();
                SetCustomTraceListeners();  // logger is reset by this.
                timer.Stop();
                Console.WriteLine("EXIT: {0} Evaluator::InitInjector. Duration: [{1}].", DateTime.Now, timer.Elapsed);
                
                using (logger.LogScope("Evaluator::Main"))
                {
                    if (IsDebuggingEnabled())
                    {
                        AttachDebugger();
                    }

                    // Register our exception handler
                    AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

                    // Fetch some settings from the ConfigurationManager
                    SetHeartbeatPeriod();
                    SetHeartbeatMaxRetry();

                    // Parse the command line
                    // The error handler RID should now be written in the configuration file instead
                    if (args.Count() != 1)
                    {
                        var e = new InvalidOperationException("must supply only the evaluator config file!");
                        Utilities.Diagnostics.Exceptions.Throw(e, logger);
                    }

                    // evaluator configuration file
                    string evaluatorConfigurationPath = args[0];

                    // Parse the evaluator configuration.
                    evaluatorConfig = new EvaluatorConfigurations(evaluatorConfigurationPath);

                    string rId = evaluatorConfig.ErrorHandlerRID;
                    ContextConfiguration rootContextConfiguration = evaluatorConfig.RootContextConfiguration;
                    Optional<TaskConfiguration> rootTaskConfig = evaluatorConfig.TaskConfiguration;
                    Optional<ServiceConfiguration> rootServiceConfig = evaluatorConfig.RootServiceConfiguration;

                    // remoteManager used as client-only in evaluator
                    IRemoteManager<REEFMessage> remoteManager = injector.GetInstance<IRemoteManagerFactory>().GetInstance(new REEFMessageCodec());
                    IRemoteIdentifier remoteId = new SocketRemoteIdentifier(NetUtilities.ParseIpEndpoint(rId));

                    RuntimeClock clock = InstantiateClock();
                    logger.Log(Level.Info, "Application Id: " + evaluatorConfig.ApplicationId);
                    EvaluatorSettings evaluatorSettings = new EvaluatorSettings(
                        evaluatorConfig.ApplicationId,
                        evaluatorConfig.EvaluatorId,
                        heartbeatPeriodInMs,
                        heartbeatMaxRetry,
                        rootContextConfiguration,
                        clock,
                        remoteManager,
                        injector);

                    HeartBeatManager heartBeatManager = new HeartBeatManager(evaluatorSettings, remoteId);
                    ContextManager contextManager = new ContextManager(heartBeatManager, rootServiceConfig, rootTaskConfig);
                    EvaluatorRuntime evaluatorRuntime = new EvaluatorRuntime(contextManager, heartBeatManager);

                    // TODO: replace with injectionFuture
                    heartBeatManager._evaluatorRuntime = evaluatorRuntime;
                    heartBeatManager._contextManager = contextManager;

                    SetRuntimeHandlers(evaluatorRuntime, clock);

                    clock.Run();
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        /// <summary>
        /// Determines whether debugging is enabled.
        /// </summary>
        /// <returns>true, if debugging is enabled</returns>
        private static bool IsDebuggingEnabled()
        {
            var debugEnabledString = Environment.GetEnvironmentVariable("Org.Apache.REEF.EvaluatorDebug");
            return !string.IsNullOrWhiteSpace(debugEnabledString) &&
                   debugEnabledString.Equals("enabled", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Waits for the debugger to be attached.
        /// </summary>
        private static void AttachDebugger()
        {
            // Wait for the debugger
            while (true)
            {
                if (Debugger.IsAttached)
                {
                    break;
                }

                logger.Log(Level.Info, "Evaluator in debug mode, waiting for debugger to be attached...");
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// Sets the heartbeat period from the ConfigurationManager
        /// </summary>
        private static void SetHeartbeatPeriod()
        {
            var heartbeatPeriodFromConfig = ConfigurationManager.AppSettings["EvaluatorHeartbeatPeriodInMs"];

            var heartbeatPeriod = 0;

            if (!string.IsNullOrWhiteSpace(heartbeatPeriodFromConfig) &&
                int.TryParse(heartbeatPeriodFromConfig, out heartbeatPeriod))
            {
                heartbeatPeriodInMs = heartbeatPeriod;
            }
            logger.Log(Level.Verbose,
                "Evaluator heartbeat period set to be " + heartbeatPeriodInMs + " milliSeconds.");
        }

        /// <summary>
        /// Sets the heartbeat retry count from the ConfigurationManager
        /// </summary>
        private static void SetHeartbeatMaxRetry()
        {
            var maxHeartbeatRetry = 0;
            var heartbeatMaxRetryFromConfig =
                ConfigurationManager.AppSettings["EvaluatorHeartbeatRetryMaxTimes"];

            if (!string.IsNullOrWhiteSpace(heartbeatMaxRetryFromConfig) &&
                int.TryParse(heartbeatMaxRetryFromConfig, out maxHeartbeatRetry))
            {
                heartbeatMaxRetry = maxHeartbeatRetry;
            }
            logger.Log(Level.Verbose,
                "Evaluator heartbeat max retry set to be " + heartbeatMaxRetry + " times.");
        }


        /// <summary>
        /// Instantiates the root injector of the Evaluator.
        /// </summary>
        /// <exception cref="EvaluatorInjectorInstantiationException">If the injector cannot be instantiated.</exception>
        private static void InitInjector()
        {
            try
            {
                injector = TangFactory.GetTang().NewInjector(ReadEvaluatorConfiguration());
            }
            catch (Exception e)
            {
                throw new EvaluatorInjectorInstantiationException(e);
            }
        }

        /// <summary>
        /// Reads the Evaluator Configuration.
        /// </summary>
        /// <exception cref="EvaluatorConfigurationFileNotFoundException">When the configuration file cannot be found.</exception>
        /// <exception cref="EvaluatorConfigurationParseException">When the configuration file exists, but can't be deserialized.</exception>
        /// <returns></returns>
        private static IConfiguration ReadEvaluatorConfiguration()
        {
            string clrRuntimeConfigurationFile = Path.Combine(Directory.GetCurrentDirectory(), "reef", "global",
                                                                 Common.Constants.ClrBridgeRuntimeConfiguration);
            if (!File.Exists(clrRuntimeConfigurationFile))
            {
                throw new EvaluatorConfigurationFileNotFoundException(clrRuntimeConfigurationFile);
            }
            
            try
            {
                return new AvroConfigurationSerializer().FromFile(clrRuntimeConfigurationFile);
            }
            catch (Exception e)
            {
                throw new EvaluatorConfigurationParseException(e);
            }
        }

        /// <summary>
        /// Instantiates the RuntimeClock
        /// </summary>
        /// <exception cref="ClockInstantiationException">When the clock can't be instantiated.</exception>
        /// <returns></returns>
        private static RuntimeClock InstantiateClock()
        {
            IConfiguration clockConfiguration = new ConfigurationModuleBuilder().Build().Build();
            try
            {
                return TangFactory.GetTang().NewInjector(clockConfiguration).GetInstance<RuntimeClock>();
            }
            catch (Exception exception)
            {
                throw new ClockInstantiationException("Unable to instantiate the clock", exception);
            } 
        }

        private static void SetCustomTraceListeners()
        {
            ISet<TraceListener> customTraceListeners;
            try
            {
                customTraceListeners = injector.GetInstance<CustomTraceListeners>().Listeners;
            }
            catch (Exception e)
            {
                Utilities.Diagnostics.Exceptions.Caught(e, Level.Error, logger);
                // custom trace listener not set properly, use empty set
                customTraceListeners = new HashSet<TraceListener>();
            }
            foreach (TraceListener listener in customTraceListeners)
            {
                Logger.AddTraceListener(listener);
            }
            logger = Logger.GetLogger(typeof(Evaluator));
            CustomTraceLevel traceLevel = injector.GetInstance<CustomTraceLevel>();
            Logger.SetCustomLevel(traceLevel.TraceLevel);
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Fail((Exception)e.ExceptionObject);
        }

        // set the handlers for runtimeclock manually
        // we only need runtimestart and runtimestop handlers now
        private static void SetRuntimeHandlers(EvaluatorRuntime evaluatorRuntime, RuntimeClock clock)
        {
            ISet<IObserver<RuntimeStart>> runtimeStarts = new HashSet<IObserver<RuntimeStart>> { evaluatorRuntime };
            clock.InjectedRuntimeStartHandler = new InjectionFutureImpl<ISet<IObserver<RuntimeStart>>>(runtimeStarts);

            ISet<IObserver<RuntimeStop>> runtimeStops = new HashSet<IObserver<RuntimeStop>> { evaluatorRuntime };
            clock.InjectedRuntimeStopHandler = new InjectionFutureImpl<ISet<IObserver<RuntimeStop>>>(runtimeStops);
        }

        private static void Fail(Exception ex)
        {
            var message = "Unhandled exception caught in Evaluator. Current files in the working directory: " +
                          string.Join(", ", Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.*", SearchOption.AllDirectories));
            Utilities.Diagnostics.Exceptions.Throw(ex, message, logger);
        }
    }
}