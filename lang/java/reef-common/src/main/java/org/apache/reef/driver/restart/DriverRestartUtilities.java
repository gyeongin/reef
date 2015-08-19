/*
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
package org.apache.reef.driver.restart;

import org.apache.reef.annotations.Unstable;
import org.apache.reef.annotations.audience.DriverSide;
import org.apache.reef.annotations.audience.Private;

/**
 * A static utilities class for simplifying calls to driver restart manager.
 * Functions here should always call driverRestartManager.canRestart() before performing any
 * actual options.
 */
@Private
@DriverSide
@Unstable
public final class DriverRestartUtilities {

  /**
   * Helper function for driver restart to determine whether an evaluator ID is from an evaluator from the
   * previous application attempt.
   */
  public static boolean isRestartAndIsPreviousEvaluator(final DriverRestartManager driverRestartManager,
                                                        final String evaluatorId) {
    return driverRestartManager.isRestart() && driverRestartManager.getPreviousEvaluatorIds().contains(evaluatorId);
  }

  private DriverRestartUtilities() {
  }
}