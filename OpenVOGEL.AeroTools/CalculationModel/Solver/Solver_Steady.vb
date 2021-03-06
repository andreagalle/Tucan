﻿'Open VOGEL (https://en.wikibooks.org/wiki/Open_VOGEL)
'Open source software for aerodynamics
'Copyright (C) 2018 Guillermo Hazebrouck (gahazebrouck@gmail.com)

'This program Is free software: you can redistribute it And/Or modify
'it under the terms Of the GNU General Public License As published by
'the Free Software Foundation, either version 3 Of the License, Or
'(at your option) any later version.

'This program Is distributed In the hope that it will be useful,
'but WITHOUT ANY WARRANTY; without even the implied warranty Of
'MERCHANTABILITY Or FITNESS FOR A PARTICULAR PURPOSE.  See the
'GNU General Public License For more details.

'You should have received a copy Of the GNU General Public License
'along with this program.  If Not, see < http:  //www.gnu.org/licenses/>.

Imports DotNumerics.LinearAlgebra
Imports OpenVOGEL.MathTools.Algebra.EuclideanSpace

Namespace CalculationModel.Solver

    Partial Public Class Solver

        ''' <summary>
        ''' Indicates if source panels are included
        ''' </summary>
        ''' <returns></returns>
        Public Property WithSources As Boolean = False

        ''' <summary>
        ''' Convect wakes and calculates loads at the last time step.
        ''' </summary>
        Public Sub SteadyState(ByVal DataBasePath As String)

            TestOpenCL()

            If Settings.UseGpu Then

                GpuVortexSolver = New GpuTools.VortexSolver
                GpuVortexSolver.Initialize(Settings.GpuDeviceId)

            End If

            Dim StartingTime As Date = Now

            CreateSubFoldersNames(DataBasePath)
            CreateSubFolder(DataBaseSection.Steady)
            CleanDirectory(DataBaseSection.Steady)

            _StreamVelocity.Assign(Settings.StreamVelocity)

            _StreamOmega.Assign(Settings.Omega)

            _StreamDensity = Settings.Density

            Dim SquareVelocity As Double = _StreamVelocity.SquareEuclideanNorm

            _StreamDynamicPressure = 0.5 * _StreamDensity * SquareVelocity

            Dim WithStreamOmega As Boolean = _StreamOmega.EuclideanNorm > 0

            Dim WakeExtension As New Vector3(Settings.StreamVelocity)
            WakeExtension.Normalize()
            WakeExtension.Scale(100.0)

            RaiseEvent PushMessage("Building doublets matrix")
            BuildMatrixForDoublets()

            If WithSources Then

                RaiseEvent PushMessage("Building sources matrix")
                BuildMatrixForSources()

                RaiseEvent PushMessage("Assigning sources")
                AssignSources(WithStreamOmega)

            End If

            RaiseEvent PushMessage("Building RHS")
            BuildRHS_I(WithStreamOmega)

            RaiseEvent PushMessage("Initializing wakes")
            InitializeWakes()

            RaiseEvent PushMessage(String.Format("Generating LU decomposition ({0})", Dimension))
            Dim LE As New LinearEquations
            LE.ComputeLU(MatrixDoublets)
            G = New Vector(Dimension)

            For TimeStep = 1 To Settings.SimulationSteps

                If CancellationPending Then
                    CancelProcess()
                    Return
                End If

                RaiseEvent PushProgress(String.Format("Step {0}", TimeStep), 100 * TimeStep / Settings.SimulationSteps)

                LE.SolveLU(RHS, G)

                AssignDoublets()

                ' Calculate induced velocity on wake NP:

                CalculateVelocityOnWakes(WithStreamOmega)

                ' Convect wake:

                For Each Lattice In Lattices

                    If WithSources And Settings.StrongWakeInfluence Then

                        Lattice.PopulateWakeRingsAndVortices(Settings.Interval, TimeStep, Settings.ExtendWakes, WakeExtension)

                    Else

                        ' We tame the root vortex when there are fuselages.

                        Lattice.PopulateWakeVortices(Settings.Interval, TimeStep, Settings.ExtendWakes, WakeExtension)

                    End If

                Next

                CalculateVelocityInducedByTheWakesOnBoundedLattices(True)

                If WithSources And Settings.StrongWakeInfluence Then

                    CalculatePotentialInducedByTheWakeOnThickBoundedLattices()

                End If

                BuildRHS_II(WithStreamOmega)

                'Next time step

            Next

            RaiseEvent PushMessage("Calculating airloads")

            ' Complete the last step

            LE.SolveLU(RHS, G)

            AssignDoublets()

            CalculateVelocityInducedByTheWakesOnBoundedLattices(False)

            ' Calculate total velocity:

            CalculateTotalVelocityOnBoundedLattices(WithStreamOmega)

            ' Calculate vortex rings Cp or DCp:

            For Each Lattice In Lattices

                Lattice.CalculatePressure(SquareVelocity)

            Next

            ' Calculate the total airload:

            CalculateAirloads()

            ' Ready

            RaiseEvent PushMessage("Writing to database")

            Me.WriteToXML(String.Format("{0}\Steady.res", Steady_Path), True)

            Dim Interval As TimeSpan = Now - StartingTime
            Dim Message As String = String.Format("Calculation finished. Elapsed time: {0}m {1}.{2}s", Interval.Minutes, Interval.Seconds, Interval.Milliseconds)
            RaiseEvent PushMessage(Message)

            RaiseEvent CalculationDone()

        End Sub

    End Class

End Namespace

