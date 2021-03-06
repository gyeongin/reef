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
package org.apache.reef.vortex.driver;

import net.jcip.annotations.NotThreadSafe;
import org.apache.reef.annotations.audience.DriverSide;
import org.apache.reef.driver.task.RunningTask;
import org.apache.reef.vortex.common.TaskletExecutionRequest;

import java.io.Serializable;
import java.util.Collection;
import java.util.HashMap;

/**
 * Representation of a VortexWorkerManager in Driver.
 */
@NotThreadSafe
@DriverSide
class VortexWorkerManager {
  private final VortexRequestor vortexRequestor;
  private final RunningTask reefTask;
  private final HashMap<Integer, Tasklet> runningTasklets = new HashMap<>();

  VortexWorkerManager(final VortexRequestor vortexRequestor, final RunningTask reefTask) {
    this.vortexRequestor = vortexRequestor;
    this.reefTask = reefTask;
  }

  <TInput extends Serializable, TOutput extends Serializable>
      void launchTasklet(final Tasklet<TInput, TOutput> tasklet) {
    assert !runningTasklets.containsKey(tasklet.getId());
    runningTasklets.put(tasklet.getId(), tasklet);
    final TaskletExecutionRequest<TInput, TOutput> taskletExecutionRequest
        = new TaskletExecutionRequest<>(tasklet.getId(), tasklet.getUserFunction(), tasklet.getInput());
    vortexRequestor.send(reefTask, taskletExecutionRequest);
  }

  <TOutput extends Serializable> Tasklet taskletCompleted(final Integer taskletId, final TOutput result) {
    final Tasklet<?, TOutput> tasklet = runningTasklets.remove(taskletId);
    assert tasklet != null; // Tasklet should complete/error only once
    tasklet.completed(result);
    return tasklet;
  }

  Tasklet taskletThrewException(final Integer taskletId, final Exception exception) {
    final Tasklet tasklet = runningTasklets.remove(taskletId);
    assert tasklet != null; // Tasklet should complete/error only once
    tasklet.threwException(exception);
    return tasklet;
  }

  Collection<Tasklet> removed() {
    return runningTasklets.isEmpty() ? null : runningTasklets.values();
  }

  void terminate() {
    reefTask.close();
  }

  String getId() {
    return reefTask.getId();
  }

  /**
   * @return the description of this worker in string.
   */
  @Override
  public String toString() {
    return "VortexWorkerManager: " + getId();
  }

  /**
   * For unit tests only.
   */
  boolean containsTasklet(final Integer taskletId) {
    return runningTasklets.containsKey(taskletId);
  }
}