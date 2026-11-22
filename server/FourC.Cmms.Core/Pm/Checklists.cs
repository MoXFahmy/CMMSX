namespace FourC.Cmms.Core.Pm;

/// <summary>What a checklist step asks for (0.13.0). Check is 0, so every step saved before 0.13.0 is a Check step.</summary>
public enum StepType { Check = 0, Number = 1, Meter = 2, Text = 3 }

/// <summary>A Check step's answer.</summary>
public enum StepAnswer { Ok = 1, NotOk = 2, NotApplicable = 3 }

/// <summary>What a failed step raises: the organization's default, nothing, a work order from a template, or a request for triage.</summary>
public enum FollowUpKind { Default = 0, None = 1, WorkOrder = 2, Request = 3 }

/// <summary>Where a reading falls against its measurement point's levels.</summary>
public enum MeasureLevel { Normal = 0, AlertHigh = 1, AlarmHigh = 2, AlertLow = 3, AlarmLow = 4 }

public enum MeasurementKind { Temperature = 1, Pressure = 2, Current = 3, Voltage = 4, Level = 5, Consumption = 6, Other = 7 }
