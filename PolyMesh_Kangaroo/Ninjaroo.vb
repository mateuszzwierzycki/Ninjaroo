Imports GH_IO.Serialization
Imports Grasshopper.Kernel
Imports Grasshopper.Kernel.Types
Imports KangarooSolver
Imports System.Windows.Forms

Public Class Ninjaroo
    Inherits GH_Component

    Sub New()
        MyBase.New("Ninja Kangaroo", "Ninjaroo", "Multithreaded Kangaroo Solver", "Kangaroo2", "Main")
    End Sub

    Public Overrides ReadOnly Property ComponentGuid As Guid
        Get
            Return New Guid("c5c79908-d2a3-44c5-9fc3-358769fd6230")
        End Get
    End Property

    Protected Overrides Sub RegisterInputParams(pManager As GH_InputParamManager)
        pManager.AddGenericParameter("Goals", "G", "Goals", GH_ParamAccess.tree)
        pManager.AddNumberParameter("Tolerance", "T", "Tolerance", GH_ParamAccess.item, 0.01)
        pManager.AddNumberParameter("Energy", "K", "Kinetic energy threshold", GH_ParamAccess.item, 0)
        pManager.AddBooleanParameter("Reset", "X", "Reset", GH_ParamAccess.item, False)
        pManager.AddBooleanParameter("Run", "R", "Run", GH_ParamAccess.item, False)
        Me.Params.Input(0).Optional = True
    End Sub

    Protected Overrides Sub RegisterOutputParams(pManager As GH_OutputParamManager)
        pManager.AddIntegerParameter("Iterations", "I", "Iterations", GH_ParamAccess.item)
        pManager.AddPointParameter("Positions", "P", "Particle positions", GH_ParamAccess.list)
        pManager.AddGenericParameter("Outputs", "O", "Goal outputs", GH_ParamAccess.list)
    End Sub

    Public Overrides Sub AddedToDocument(document As GH_Document)
        TurnItOff()
    End Sub

    Public Overrides Sub DocumentContextChanged(document As GH_Document, context As GH_DocumentContext)
        MyBase.DocumentContextChanged(document, context)
        TurnItOff()
    End Sub

    Public Overrides Sub MovedBetweenDocuments(oldDocument As GH_Document, newDocument As GH_Document)
        MyBase.MovedBetweenDocuments(oldDocument, newDocument)
        TurnItOff()
    End Sub

    'component variables
    Private _Running As Boolean = False
    Private _Energy As Double = 0.01
    Private _Reset As Boolean = False
    Private _Solver As New PhysicalSystem
    Private _Tolerance As Double = 0.01

    Private _OutputGeometry As Boolean = True

    Private Property OutputGeometry As Boolean
        Get
            Return _OutputGeometry
        End Get
        Set(value As Boolean)
            _OutputGeometry = value
        End Set
    End Property

    Public Overrides Function Read(reader As GH_IReader) As Boolean
        _OutputGeometry = reader.GetBoolean("geo")
        Return MyBase.Read(reader)
    End Function

    Public Overrides Function Write(writer As GH_IWriter) As Boolean
        writer.SetBoolean("geo", _OutputGeometry)
        Return MyBase.Write(writer)
    End Function

    Private Sub GeoSwitch()
        _OutputGeometry = Not _OutputGeometry
    End Sub

    Protected Overrides Sub AppendAdditionalComponentMenuItems(menu As ToolStripDropDown)
        Menu_AppendItem(menu, "Output Geometry", AddressOf GeoSwitch, True, _OutputGeometry)
        MyBase.AppendAdditionalComponentMenuItems(menu)
    End Sub

    Private Property Running As Boolean
        Get
            Return _Running
        End Get
        Set(value As Boolean)
            _Running = value
        End Set
    End Property

    Private Property KineticEnergy As Double
        Get
            Return _Energy
        End Get
        Set(value As Double)
            _Energy = value
        End Set
    End Property

    Private Property Reset As Boolean
        Get
            Return _Reset
        End Get
        Set(value As Boolean)
            _Reset = value
        End Set
    End Property

    Private Property Kangaroo As PhysicalSystem
        Get
            Return _Solver
        End Get
        Set(value As PhysicalSystem)
            _Solver = value
        End Set
    End Property

    Private Property Tolerance As Double
        Get
            Return _Tolerance
        End Get
        Set(value As Double)
            _Tolerance = value
        End Set
    End Property

    Protected Overrides Sub SolveInstance(DA As IGH_DataAccess)
        If Not GetAllData(DA, MainGoals) Then Return

        If Reset Then 'the thread is already over so we can do whatever
            TurnItOff()
            Me.Message = "Reset"
        Else
            If Running Then
                If Converged Then
                    Me.Message = "Converged"
                Else
                    If BackThread Is Nothing Then 'if its not running yet then we create it
                        BackThread = New Threading.Thread(AddressOf Compute)
                        BackThread.Start()
                    End If

                    Me.Message = "Running"
                End If
            Else
                Me.Message = ""
            End If
        End If

        While Not MainOutput(DA) 'collects goals from the thread and outputs them
        End While
    End Sub

    Private Function GetAllData(DA As IGH_DataAccess, ByRef Goals As List(Of IGoal)) As Boolean
        If Not ThreadCollectingSignal Then
            MainCollectingSignal = True
            Goals.Clear()

            If Not DA.GetData(1, Tolerance) Then Return Nothing
            If Not DA.GetData(2, KineticEnergy) Then Return Nothing
            If Not DA.GetData(3, Reset) Then Return Nothing
            If Not DA.GetData(4, Running) Then Return Nothing

            For Each GD As Object In Me.Params.Input(0).VolatileData.AllData(True)
                If TryCast(GD, IEnumerable(Of IGoal)) IsNot Nothing Then
                    Dim gho As List(Of IGoal) = CType(GD, List(Of IGoal))
                    Goals.AddRange(gho)
                ElseIf TryCast(GD, IGoal) IsNot Nothing Then
                    Dim gho As IGoal = CType(GD, IGoal)
                    Goals.Add(gho)
                ElseIf TryCast(GD, GH_ObjectWrapper) IsNot Nothing Then
                    Dim wr As GH_ObjectWrapper = GD

                    If TryCast(wr.Value, IEnumerable(Of IGoal)) IsNot Nothing Then
                        Dim gho As List(Of IGoal) = CType(wr.Value, List(Of IGoal))
                        Goals.AddRange(gho)
                    ElseIf TryCast(wr.Value, IGoal) IsNot Nothing Then
                        Dim gho As IGoal = CType(wr.Value, IGoal)
                        Goals.Add(gho)
                    End If
                End If
            Next

            MainCollectingSignal = False
            Return True
        Else
            Return False
        End If
    End Function

    Private Function MainOutput(DA As IGH_DataAccess) As Boolean
        If Not ThreadOutputtingData Then
            MainOutputingData = True
            DA.SetData(0, Kangaroo.GetIterations)
            DA.SetDataList(1, MainPos)

            If OutputGeometry Then
                DA.SetDataList(2, MainOut)
            Else
                Dim temp As New List(Of GH_ObjectWrapper)
                For i As Integer = 0 To MainOut.Count - 1 Step 1
                    temp.Add(New GH_ObjectWrapper(MainOut(i)))
                Next
                DA.SetDataList(2, temp)
            End If

            MainOutputingData = False
            Return True
        End If
        Return False
    End Function

    Dim BackThread As Threading.Thread = Nothing
    Private Converged As Boolean = False

    Private MainGoals As New List(Of IGoal)
    Private MainPos As New List(Of GH_Point)
    Private MainOut As New List(Of Object)

    Private ThreadGoals As New List(Of IGoal)
    Private ThreadPos As New List(Of GH_Point)
    Private ThreadOut As New List(Of Object)

    Private MainCollectingSignal As Boolean = False 'when the components is collecting the goals into maingoals
    Private ThreadCollectingSignal As Boolean = False 'when the thread is gathering the goals from the main list

    Private MainOutputingData As Boolean = False
    Private ThreadOutputtingData As Boolean = False

    Private Function ThreadCollectGoals(ByRef Goals As List(Of IGoal)) As Boolean
        If Not MainCollectingSignal Then
            ThreadCollectingSignal = True
            Goals.Clear()
            Goals.AddRange(MainGoals)
            ThreadCollectingSignal = False
            Return True
        Else
            Return False
        End If
    End Function

    Private Function ThreadOutputGeometry() As Boolean
        If Not MainOutputingData Then
            ThreadOutputGeometry = True
            MainPos.Clear()
            MainPos.AddRange(ThreadPos)
            MainOut.Clear()
            MainOut.AddRange(ThreadOut)
            ThreadOutputGeometry = False
            Return True
        End If
        Return False
    End Function

    Public Sub Compute()
        Converged = False
        Dim tim As New Stopwatch()
        tim.Start()
        Dim FirstCollect As Boolean = False


        Try
            Do
                If Not Running Then
                    Exit Sub
                End If

                If tim.ElapsedMilliseconds > 500 Or FirstCollect = False Then
                    While Not ThreadCollectGoals(ThreadGoals)
                    End While

                    For Each g As IGoal In ThreadGoals
                        If Not Running Then Exit Sub
                        If g.PIndex Is Nothing Then
                            Kangaroo.AssignPIndex(g, Tolerance)
                        End If
                    Next

                    ThreadPos = Kangaroo.GetPositionsGH.ToList
                    ThreadOut = Kangaroo.GetOutput(ThreadGoals)

                    While Not ThreadOutputGeometry()
                    End While

                    If FirstCollect Then
                        InvokeExpire()
                    End If

                    FirstCollect = True
                    tim.Restart()
                End If

                For i As Integer = 0 To 10 - 1
                    If Running Then Kangaroo.Step(ThreadGoals, True, 0)
                    If tim.ElapsedMilliseconds > 2000 Then Exit For
                Next

                If Kangaroo.GetvSum < KineticEnergy Then
                    Converged = True
                    While Not ThreadOutputGeometry()
                    End While
                    Exit Do
                End If
            Loop

            InvokeExpire()

        Catch ex As Exception
            Me.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Try restarting.")
            Me.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message)
        End Try

    End Sub

    Private exp As Action = AddressOf ExpireComponent

    Private Sub ExpireComponent()
        Me.ExpireSolution(True)
    End Sub

    Private Sub InvokeExpire()
        If Rhino.RhinoApp.MainApplicationWindow.InvokeRequired Then Rhino.RhinoApp.MainApplicationWindow.Invoke(exp)
    End Sub

    ''' <summary>
    ''' This makes sure the thread is not running anymore
    ''' </summary>
    Private Sub TurnItOff()
        Running = False
        If BackThread IsNot Nothing Then BackThread.Join()
        BackThread = Nothing
        Converged = False
        Me.Kangaroo.ClearParticles()
        Me.Kangaroo.Restart()

        For Each g As IGoal In MainGoals
            If g.PPos IsNot Nothing Then
                g.PIndex = Nothing
            End If
        Next

        If MainPos IsNot Nothing Then MainPos.Clear()
        If MainOut IsNot Nothing Then MainOut.Clear()
        If ThreadPos IsNot Nothing Then ThreadPos.Clear()
        If ThreadOut IsNot Nothing Then ThreadOut.Clear()

        MainCollectingSignal = False
        ThreadCollectingSignal = False
        MainOutputingData = False
        ThreadOutputtingData = False

    End Sub
End Class
