using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

[assembly: AssemblyVersion("1.0.0.1")]



/* * 
 * TODO: the person that created this plan should NOT run the script, and should be physicist, and part of the SBRT group.
 * OR Only in status planning approved?, members of the SBRT group might change...
 * hard to check if the documents with coordinates exists
 * 
 * Patient/image orientation:
 * LAT: methods should work regardless, only when calculating the final SBRT coordinate value has this to be taken into consideration
 * VRT: works for HFS and FFS, lot of checking to do if this is going to work for HFP and FFP... need to pass on planorientation or imageorientation
 * LNG: Measures directly in the frame, need to take care of this when working with relative distances in dicom coordinates
 * 
 * Start by getting frame of reference for the SBF in Lat and Vrt in the Lng-coordinate specified by the point of interest (user origo, isocenter or SBF setup marker)
 * 
 * Double checking:
 * Vrt coordinate is double checked by taking a lateral profile 10 mm above the SBF bottom, i.e. where the frame widens, and comparing the found width of the SBF with the expected value.
 * Lat coordinate is double checked simply by comparing the found width of the SBF with the expected value.
 * Long is compared left side to right side, and is the parameter most likely to fail for profile measurement, depending on image quality, slice thickness and fidusle condition 
 * */


/**
 * Cases:
 * 12 cases...
 * switch with long as long only != 0 if vrt and lat both found
 * 
 * 
 * 
 *
 * 
 * 
 * 
 * 
**/


namespace VMS.TPS
{
	public class Script
	{
		public Script()
		{
		}

		enum CheckResults
		{
			Found,
			NoLong,
			NotFound,
			NotOK
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Execute(ScriptContext context /*, System.Windows.Window window, ScriptEnvironment environment*/)
		{

			// Two use cases;
			// 1: planSetup on planning CT, check origo, iso and SBF setup
			// 2: planSetup on 4DCT phase, might mean that the check of the SBF setup coordinates have to be done on a separate image without plan.
			// TODO: search for image (single) in the same study UID registered to the planning image with the structure SBF setup in the structure set, might be different FOR
			// due to 4DCT sometimes taken FFS, if found in planning image, do not search for it...
			if (context.Patient == null)
			{
				MessageBox.Show("Please select a patient and plan/image in active context window.");
			}
			else if (context.PlanSetup == null && context.StructureSet.Image != null)
			{
				// (image.ImagingOrientation.Equals("HeadFirstSupine") || image.ImagingOrientation.Equals("FeetFirstSupine"))
				Image image = context.StructureSet.Image;
				var OrigoCheckResults = CheckResults.NotFound;
				int origoLong = 0;
				if (image.HasUserOrigin)
                {
					string checkSBRTOrigo = CheckUserOriginInSBRTFrame(image, ref OrigoCheckResults, ref origoLong);  // should really refactor this
					MessageBox.Show(checkSBRTOrigo);
				}
                
				//string checkSBRTOrigo = CheckUserOriginInSBRTFrame(plan);
				// this might be tricky... image may be different orientation, have to rewrite or overload methods, taking an image instead of a plan, probably overload
				// might have to use                 context.Image.UserToDicom
			}
			else if (context.PlanSetup != null && context.PlanSetup.StructureSet.Image != null)
            {
				PlanSetup plan = context.PlanSetup;
				Image image = plan.StructureSet.Image;
				int origoLong = 0;

				var OrigoCheckResults = CheckResults.NotFound;

				string checkSBRTOrigo = CheckUserOriginInSBRTFrame(image, ref OrigoCheckResults, ref origoLong);  // should really refactor this
				MessageBox.Show(checkSBRTOrigo);
				string checkSBRTiso = GetIsoCoordInSBRTFrame(plan, OrigoCheckResults, origoLong);
				MessageBox.Show(checkSBRTiso);
				string checkSBFsetup = GetSBFSetupCoord(plan);
				MessageBox.Show(checkSBFsetup);


                //string isoCoordInSBRT = GetIsoCoordInSBRTFrame(plan);

            }
            else
            {
				MessageBox.Show("Please select a patient and plan/image in active context window.");
			}

		}


		// ********* Helper method for checking iso coordinates, returns VVector  
		// transforms coordinates from dicom to coordinates based on coronal view from table end, dicom-origo the same (usually center of image in Lat, below table in vrt)
		// TODO: check if this clashes with other properties in VVector   TODO: check if all fields same isocenter
		// TODO: would be nice if coordinates instead originates from center of image (or center of couch) in Lat, and Couch top surface in Vrt (this would also mean a 
		// chance to predict/estimate absolute couch coordinates in lat and vrt)
		public VVector IsoPositionFromTableEnd(PlanSetup plan)
		{
			//var image = plan.StructureSet.Image;
			VVector planIso = plan.Beams.First().IsocenterPosition; // mm from Dicom-origo
			switch (plan.TreatmentOrientation.ToString())
			{
				case "FeetFirstSupine":
					{
						planIso.x *= -1;
						break;
					}
				case "FeetFirstProne":
					{
						planIso.y *= -1;
						break;
					}
				case "HeadFirstProne":
					{
						planIso.x *= -1;
						planIso.y *= -1;
						break;
					}
				default:
					break;
			}
			return planIso;
		}


		//*********HELPER METHODS**************
		/// <summary>
		/// SameSign; helper method to determine if two doubles have the same sign
		/// </summary>
		/// <param name="num1"></param>
		/// <param name="num2"></param>
		/// <returns></returns>
		bool SameSign(double num1, double num2)
		{
			return num1 >= 0 && num2 >= 0 || num1 < 0 && num2 < 0;
		}

		public class PatternGradient
		{
			public List<double> DistanceInMm { get; set; }
			public List<int> GradientHUPerMm { get; set; }
			public List<double> MinGradientLength { get; set; }
			public List<int> PositionToleranceMm { get; set; }
			public int GradIndexForCoord { get; set; }
		}

		/*
		public VVector SBRTPatPosition(PlanSetup plan)
		{
			//var image = plan.StructureSet.Image;
			VVector transformToSBRTDir = plan.Beams.First().IsocenterPosition; // mm from Dicom-origo
			switch (plan.TreatmentOrientation.ToString())
			{
				case "FeetFirstSupine":
					{
						transformToSBRTDir.x *= -1;
						break;
					}
				case "FeetFirstProne":
					{
						transformToSBRTDir.y *= -1;
						break;
					}
				case "HeadFirstProne":
					{
						transformToSBRTDir.x *= -1;
						transformToSBRTDir.y *= -1;
						break;
					}
				default:
					break;
			}
			return transformToSBRTDir;
		}

		*/



		/// <summary>
		/// 
		/// </summary>
		/// <param name="plan"></param>
		/// <returns></returns>
		private string CheckUserOriginInSBRTFrame(Image image, ref CheckResults origoCheckResults, ref int origoLong)
		{
			string userOrigoCheck;
			
			double bottom;
			double left;                // left and right side to pass it on to method to get longcoordinates
			double right;               // might not be actual left or right, simply a way to denote the two different sides
			int positionTolerance = 3;	// Tolerance for accepting the position of user origo and returning coordinates based on set user origo for comparison

			var coord = GetTransverseCoordInSBRTFrame(image, image.UserOrigin);
			left = coord[0];
			right = coord[1];
			bottom = coord[2];
			double lateralCenterSBRT = (left + right) / 2; // Dicom position of the lateral center of the SBF

			double coordSRSLong = GetSRSLongCoord(image, image.UserOrigin, (int)Math.Round(bottom), left, right);

			int userOrigoLatSRS = (int)(Math.Round(lateralCenterSBRT + 300 - image.UserOrigin.x));		// TODO: this works for HFS and FFS. HFP and FFP unhandled
			int userOrigoVrtSRS = (int)(Math.Round(bottom - image.UserOrigin.y));                       // TODO: this works for HFS and FFS. HFP and FFP unhandled
			int userOrigoLongSRS = (int)Math.Round(coordSRSLong);

			if (bottom == 0)
			{
				userOrigoCheck = "Cannot find the SBF in user origo plane, no automatic check of User origo possible.";
				origoCheckResults = CheckResults.NotFound;
			}
			else if (userOrigoLongSRS == 0 && userOrigoVrtSRS < (95 + positionTolerance) && userOrigoVrtSRS > (95 - positionTolerance))
			{
				userOrigoCheck = "Cannot find the SBF long coordinate of the user origo with profiles. Estimated position of origo in SBRT frame coordinates in transverse direction from image profiles: \n\n" +
				" Lat: " + userOrigoLatSRS + "\t Vrt: " + userOrigoVrtSRS + "\n";
				origoCheckResults = CheckResults.NoLong;
			}
			else if (userOrigoVrtSRS < (95 + positionTolerance) && userOrigoVrtSRS > (95 - positionTolerance) && Math.Abs(image.UserOrigin.x - lateralCenterSBRT) < positionTolerance)
			{
				userOrigoCheck = "Estimated position of user origin in SBF coordinates from image profiles: \n\n" +
				" Lat: " + userOrigoLatSRS + "\t Vrt: " + userOrigoVrtSRS + "\t Lng: " + userOrigoLongSRS + "\t (+/- 3mm)" +
				"\n";
				origoCheckResults = CheckResults.Found;
				origoLong = userOrigoLongSRS;
			}
			else
			{
				userOrigoCheck = "* Check the position of user origo.";
				origoCheckResults = CheckResults.NotOK;
			}
			return userOrigoCheck;
		}
		/*
		private int[] GetUserOriginInSBRTFrame(Image image)
        {
			
			double bottom;
			double left;                // left and right side to pass it on to method to get longcoordinates
			double right;               // might not be actual left or right, simply a way to denote the two different sides
			

			var coord = GetTransverseCoordInSBRTFrame(image, image.UserOrigin);
			left = coord[0];
			right = coord[1];
			bottom = coord[2];
			double lateralCenterSBRT = (left + right) / 2; // Dicom position of the lateral center of the SBF

			double coordSRSLong = GetSRSLongCoord(image, image.UserOrigin, (int)Math.Round(bottom), left, right);

			int userOrigoLatSRS = (int)(Math.Round(lateralCenterSBRT + 300 - image.UserOrigin.x));     // TODO: this works for HFS and FFS. HFP and FFP unhandled
			int userOrigoVrtSRS = (int)(Math.Round(bottom - image.UserOrigin.y));
			int userOrigoLongSRS = (int)Math.Round(coordSRSLong);
		}

		*/


		private string GetIsoCoordInSBRTFrame(PlanSetup plan, CheckResults origoCheckResults, int origoLong)
		{
			string isoSBRTresults = "";

			var image = plan.StructureSet.Image;
			var iso = plan.Beams.First().IsocenterPosition; // assumes that check have been done that all beams have same isocenter
			double bottom;
			double left;                
			double right;

			var coord = GetTransverseCoordInSBRTFrame(image, plan.Beams.First().IsocenterPosition);
			left = coord[0];
			right = coord[1];
			bottom = coord[2];
			double lateralCenterSBRT = (left + right) / 2;
			double coordSRSLong = GetSRSLongCoord(image, plan.Beams.First().IsocenterPosition, (int)Math.Round(bottom), left, right);

			//  Calculate the final values in SBRT frame coordinates

			int isoLatSBRT = (int)(Math.Round(lateralCenterSBRT + 300 - iso.x));                // TODO: this works for HFS and FFS. HFP and FFP unhandled
			int isoVrtSBRT = (int)(Math.Round(bottom - iso.y));
			int isoLongSRS = (int)Math.Round(coordSRSLong);

			// Get Vrt and Lat calculated directly from user origo (dicom coordinate for transverse and found long for long) for comparison if check of user origo ok

			int isoLatSBRTFromUO = (int)(Math.Round(-(iso.x - image.UserOrigin.x - 300)));         // TODO: this works for HFS and FFS. HFP and FFP unhandled
			int isoVrtSBRTFromUO = (int)(Math.Round(Math.Abs(iso.y - image.UserOrigin.y - 95)));   // TODO: this works for HFS and FFS. HFP and FFP unhandled
			int isoLngSBRTFromUO = (int)(Math.Round(Math.Abs(iso.z - image.UserOrigin.z + origoLong))); // TODO: THIS WORKS ONLY FOR HFS!!!!!!!!!!

			var isoCheckResults = CheckResults.NotFound;

			string measuredIsoCoord = "";

			if (bottom == 0)    // bottom set to 0 if bottom OR lat not found
			{
				isoCheckResults = CheckResults.NotFound;
			}
			else if (isoLongSRS == 0)
			{
				isoCheckResults = CheckResults.NoLong;
				measuredIsoCoord = " Lat: " + isoLatSBRT + "\t Vrt: " + isoVrtSBRT + "\t (+/- 3mm)";
			}
			else
			{
				isoCheckResults = CheckResults.Found;
				measuredIsoCoord = " Lat: " + isoLatSBRT + "\t Vrt: " + isoVrtSBRT + "\t Lng: " + isoLongSRS + "\t (+/- 3mm)";
			}


			// Exists 12 different cases that needs to be covered...

			string userOrigoAssumedCorrect = "(calculated from user origo in paranthesis, assuming user origo is correctly positioned in Lat: 300 and Vrt: 95):";
			string origoFound = "(calculated from user origo in paranthesis):";
			string isoNotFound = "Cannot find the SBRT frame in isocenter plane, no automatic check of isocenter possible. ";
			string isoLongNotFound = "Cannot find the isocenter long coordinate in the SBF with profiles. Estimated position of isocenter in SBF coordinates in transverse direction from image profiles ";
			string isoFound = "Estimated position of isocenter in SBF coordinates from image profiles ";
			string warningDiscrepancy = "\n\n * WARNING: discrepancy found between coordinates measured and calculated from user origo";

			switch (origoCheckResults)
            {
                case CheckResults.Found:

					string calcIsoCoord = "(Lat: " + isoLatSBRTFromUO + ")\t(Vrt: " + isoVrtSBRTFromUO + ")\t(Lng: " + isoLngSBRTFromUO + ")";

					switch (isoCheckResults)
					{
						case CheckResults.Found:

							isoSBRTresults = isoFound + origoFound + "\n\n" + measuredIsoCoord + "\n" + calcIsoCoord ;
							if (!CheckCoordAgreement(isoLatSBRT, isoLatSBRTFromUO, isoVrtSBRT, isoVrtSBRTFromUO, isoLongSRS, isoLngSBRTFromUO))
							{
								isoSBRTresults += warningDiscrepancy;
							}
							break;
						case CheckResults.NoLong:

							isoSBRTresults = isoLongNotFound + origoFound + "\n\n" + measuredIsoCoord + "\n" + calcIsoCoord;
							if (!CheckCoordAgreement(isoLatSBRT, isoLatSBRTFromUO, isoVrtSBRT, isoVrtSBRTFromUO))
							{
								isoSBRTresults += warningDiscrepancy;
							}
							break;
						case CheckResults.NotFound:

							isoSBRTresults = isoNotFound + userOrigoAssumedCorrect + "\n\n" + calcIsoCoord;
							break;

						default:
							break;
					}


					break;
                case CheckResults.NoLong:

					string calcIsoCoordNoLong = "(Lat: " + isoLatSBRTFromUO + ")\t(Vrt: " + isoVrtSBRTFromUO + ")";

					switch (isoCheckResults)
					{
						case CheckResults.Found:

							isoSBRTresults = isoFound + origoFound + "\n\n" + measuredIsoCoord + "\n" + calcIsoCoordNoLong;
							if (!CheckCoordAgreement(isoLatSBRT, isoLatSBRTFromUO, isoVrtSBRT, isoVrtSBRTFromUO))
							{
								isoSBRTresults += warningDiscrepancy;
							}
							break;
						case CheckResults.NoLong:

							isoSBRTresults = isoLongNotFound + origoFound + "\n\n" + measuredIsoCoord + "\n" + calcIsoCoordNoLong;
							if (!CheckCoordAgreement(isoLatSBRT, isoLatSBRTFromUO, isoVrtSBRT, isoVrtSBRTFromUO))
							{
								isoSBRTresults += warningDiscrepancy;
							}
							break;
						case CheckResults.NotFound:

							isoSBRTresults = isoNotFound + userOrigoAssumedCorrect + "\n\n" + calcIsoCoordNoLong;
							break;

						default:
							break;
					}

					break;
                case CheckResults.NotFound:

                    switch (isoCheckResults)
                    {
                        case CheckResults.Found:
							
							isoSBRTresults = isoFound + userOrigoAssumedCorrect + "\n\n" + measuredIsoCoord + "\n (Lat: " + isoLatSBRTFromUO + ")\t (Vrt: " + isoVrtSBRTFromUO + ")";
							if (!CheckCoordAgreement(isoLatSBRT, isoLatSBRTFromUO, isoVrtSBRT, isoVrtSBRTFromUO))
							{
								isoSBRTresults += warningDiscrepancy;
							}
							break;
                        case CheckResults.NoLong:
							
							isoSBRTresults = isoLongNotFound + userOrigoAssumedCorrect + "\n\n" + measuredIsoCoord + "\n (Lat: " + isoLatSBRTFromUO + ")\t (Vrt: " + isoVrtSBRTFromUO + ")";
							if (!CheckCoordAgreement(isoLatSBRT, isoLatSBRTFromUO, isoVrtSBRT, isoVrtSBRTFromUO))
							{
								isoSBRTresults += warningDiscrepancy;
							}
							break;
                        case CheckResults.NotFound:
							
							isoSBRTresults = isoNotFound + userOrigoAssumedCorrect + "\n\n" + "(Lat: " + isoLatSBRTFromUO + ")\t (Vrt: " + isoVrtSBRTFromUO + ")";
							break;

                        default:
                            break;
                    }
					break;

                case CheckResults.NotOK:

					switch (isoCheckResults)
					{
						case CheckResults.Found:

							isoSBRTresults = isoFound + "\n\n" + measuredIsoCoord;

							break;
						case CheckResults.NoLong:

							isoSBRTresults = isoLongNotFound + "\n\n" + measuredIsoCoord;

							break;
						case CheckResults.NotFound:

							isoSBRTresults = isoNotFound;
							break;

						default:
							break;
					}

					break;
                default:
                    break;
            }
			return isoSBRTresults;
		}


		private bool CheckCoordAgreement(int lat1, int lat2, int vrt1, int vrt2)
        {
			int tolerance = 3;
			bool lat = Math.Abs(lat1 - lat2) < tolerance;
			bool vrt = Math.Abs(vrt1 - vrt2) < tolerance;
			return lat && vrt;
        }

		private bool CheckCoordAgreement(int lat1, int lat2, int vrt1, int vrt2, int lng1, int lng2)
		{
			int tolerance = 3;
			bool lat = Math.Abs(lat1 - lat2) < tolerance;
			bool vrt = Math.Abs(vrt1 - vrt2) < tolerance;
			bool lng = Math.Abs(lng1 - lng2) < tolerance;
			return lat && vrt && lng;
		}


		// get the lat and long coordinates for the Stereotactic Body Frame setup marker used to position the patient in the SBF
		public string GetSBFSetupCoord(PlanSetup plan)
		{
			string resultSBFsetup;
			string searchForStructure = "zSBF_setup"; // there can be only one with the unique ID, Eclipse is also case sensitive

			Structure structSBFMarker = plan.StructureSet.Structures.Where(s => s.Id == searchForStructure).SingleOrDefault();

			if (structSBFMarker == null)
			{
				resultSBFsetup = "* No SBF marker or structure with ID 'zSBF_setup' found. \n";
			}
			else
			{
				var image = plan.StructureSet.Image;
				double bottom;
				double left;
				double right;

				var coord = GetTransverseCoordInSBRTFrame(image, structSBFMarker.CenterPoint);
				left = coord[0];
				right = coord[1];
				bottom = coord[2];
				double lateralCenterSBRT = (left + right) / 2;

				double coordSRSLong = GetSRSLongCoord(image, structSBFMarker.CenterPoint, (int)Math.Round(bottom), left, right);

				int setupSBFLat = Convert.ToInt32(Math.Round(lateralCenterSBRT + 300 - structSBFMarker.CenterPoint.x));     // TODO: this works for HFS and FFS. HFP and FFP unhandled
				int setupSBFLong = (int)Math.Round(coordSRSLong);

				// Get Lat calculated directly from user origo for comparison

				// !!!!!!! check this, its blaha 
				int setupSBFFromUO = (int)(Math.Round(-(structSBFMarker.CenterPoint.x - image.UserOrigin.x - 300)));         // TODO: this works for HFS and FFS. HFP and FFP unhandled

				if (bottom == 0 && setupSBFLong == 0)
				{
					resultSBFsetup = "Cannot find the SBRT frame, no automatic check of SBF setup marker possible. Lateral calculated from user origo in paranthesis, assuming user origo is correctly positioned in Lat 300 and Vrt 95: \n\n" +
										"\n (Lat: " + setupSBFFromUO + ")\t";
				}
				else if (setupSBFLong == 0)
				{
					resultSBFsetup = "Cannot find the SBRT frame long coordinate with profiles. Estimated position of SBF setup marker in lateral direction from image profiles (calculated from user origo in paranthesis): \n\n" +
					" Lat: " + setupSBFLat + "\t Vrt: " + "\n (Lat: " + setupSBFFromUO + ")";
				}
				else
				{
					resultSBFsetup = "Estimated position of SBF setup marker from image profiles (calculated from user origo in paranthesis): \n\n" +
					" Lat: " + setupSBFLat + "\t Lng: " + setupSBFLong + "\t (+/- 3mm)" +
					"\n (Lat: " + setupSBFFromUO + ")";
				}
			}

			return resultSBFsetup;
		}








		/// <summary>
		/// Gets frame of reference in the SBRT frame by returning the dicom coordinate of the SBRT frame bottom (represents 0 in frame coordinates)
		/// and left and right side of the frame in lateral direction, in the plane given by "dicomPosition"
		/// </summary>
		/// <param name="plan"></param>
		/// <param name="dicomPosition"></param>
		/// <returns></returns>
		public double[] GetTransverseCoordInSBRTFrame(Image image, VVector dicomPosition)
		{
			VVector frameOfRefSBRT = dicomPosition;
			int expectedWidth = 442;
			int widthTolerance = 2;

			// get the dicom-position representing vertical coordinate 0 in the SBRT frame
			frameOfRefSBRT.y = GetSBRTBottomCoord(image, dicomPosition);    // igores the position of dicomPosition in x and y and takes only z-position, takes bottom and center of image

			VVector frameOfRefSBRTLeft = frameOfRefSBRT;                    // TODO: left and right designation depending of HFS FFS etc...
			VVector frameOfRefSBRTRight = frameOfRefSBRT;
			double[] returnCoo = new double[3];


			// If bottom found, call method to dubblecheck that the frameOfRefSBRT.y really is the bottom of the SBRT-frame by taking profiles in the sloping part of the SBRT frame and comparing
			// width with expected width at respective height above the bottom
			if (frameOfRefSBRT.y != 0 && DoubleCheckSBRTVrt(image, frameOfRefSBRT))
			{
				double[] coordSRSLat = new double[2];
				coordSRSLat = GetSBRTLatCoord(image, dicomPosition, (int)Math.Round(frameOfRefSBRT.y));
				frameOfRefSBRTLeft.x = coordSRSLat[0];
				frameOfRefSBRTRight.x = coordSRSLat[1];
				frameOfRefSBRT.x = (frameOfRefSBRTLeft.x + frameOfRefSBRTRight.x) / 2;  // middle of the SBRT frame in Lat
				// the chances that the dicom coord in x actually is 0.0 is probably small but need to handle this by setting vrt to 0 if no sides found, TODO: could perhaps use nullable ref types 
				// Double check that the found width of the SBRT frame is as expected, allow for some flex of the frame and uncertainty of measurement. 
				if (frameOfRefSBRTLeft.x == 0 || frameOfRefSBRTRight.x == 0 || Math.Abs(frameOfRefSBRTLeft.x - frameOfRefSBRTRight.x) < expectedWidth - widthTolerance || Math.Abs(frameOfRefSBRTLeft.x - frameOfRefSBRTRight.x) > expectedWidth + widthTolerance)
				{
					frameOfRefSBRT.x = 0;
					frameOfRefSBRT.y = 0;
				}
			}
			else
			{
				frameOfRefSBRT.y = 0; // if double check of vrt failes or bottom not found
			}

			returnCoo[0] = frameOfRefSBRTLeft.x;
			returnCoo[1] = frameOfRefSBRTRight.x;
			returnCoo[2] = frameOfRefSBRT.y;
			return returnCoo;

			// TODO check if isocenter in same plane as user origo, not neccesary though as there can be multiple isocenters (muliple plans) and there is no strict rule...
		}

		private bool DoubleCheckSBRTVrt(Image image, VVector frameOfRefSBRT)
		{
			// 10 mm above the bottom the expected width of the frame is 349 mm (third gradient, i.e. the inner surface of the inner wall) Changes fast with height...
			// approx. 3.46 mm in width per mm in height ( 2*Tan(50) ) , i.e. ca +/-5 mm for +/-2 mm uncertainty in vrt
			bool checkResult = false;
			int expectedWidth = 349;
			int widthTolerans = 5;
			double xLeftUpperCorner = image.Origin.x - image.XRes / 2;  // Dicomcoord in upper left corner ( NOT middle of voxel in upper left corner)
			VVector leftProfileStart = frameOfRefSBRT;                // only to get the z-coord of the passed in VVector, x and y coord will be reassigned
			VVector rightProfileStart = frameOfRefSBRT;               // only to get the z-coord of the passed in VVector, x and y coord will be reassigned
			leftProfileStart.x = xLeftUpperCorner + image.XRes;         // start 1 pixel in left side
			rightProfileStart.x = xLeftUpperCorner + image.XSize * image.XRes - image.XRes;         // start 1 pixel in right side
			leftProfileStart.y = frameOfRefSBRT.y - 10;                 // 10 mm from assumed bottom    
			rightProfileStart.y = leftProfileStart.y;
			double stepsX = image.XRes;             //   (mm/voxel) to make the steps 1 pixel wide, can skip this if 1 mm steps is wanted

			VVector leftProfileEnd = leftProfileStart;
			VVector rightProfileEnd = rightProfileStart;
			leftProfileEnd.x += 200 * stepsX;
			rightProfileEnd.x -= 200 * stepsX;

			var samplesX = (int)Math.Ceiling((leftProfileStart - leftProfileEnd).Length / stepsX);

			var profLeft = image.GetImageProfile(leftProfileStart, leftProfileEnd, new double[samplesX]);
			var profRight = image.GetImageProfile(rightProfileStart, rightProfileEnd, new double[samplesX]);

			List<double> valHULeft = new List<double>();
			List<double> cooLeft = new List<double>();
			for (int i = 0; i < samplesX; i++)
			{
				valHULeft.Add(profLeft[i].Value);
				cooLeft.Add(profLeft[i].Position.x);
			}

			List<double> valHURight = new List<double>();
			List<double> cooRight = new List<double>();
			for (int i = 0; i < samplesX; i++)
			{
				valHURight.Add(profRight[i].Value);
				cooRight.Add(profRight[i].Position.x);
			}


			//***********  Gradient patter describing expected profile in HU of the Lax-box slanted side, from outside to inside **********

			PatternGradient sbrtSide = new PatternGradient();
			sbrtSide.DistanceInMm = new List<double>() { 0, 2, 20 };
			sbrtSide.GradientHUPerMm = new List<int>() { 100, -100, 100 };
			sbrtSide.PositionToleranceMm = new List<int>() { 0, 3, 3 };                        // tolerance for the gradient position
			sbrtSide.GradIndexForCoord = 2;                      // index of gradient position to return (zero based index), i.e. the start of the inner wall

			double[] coordBoxLat = new double[2];
			coordBoxLat[0] = GetCoordinates(cooLeft, valHULeft, sbrtSide.GradientHUPerMm, sbrtSide.DistanceInMm, sbrtSide.PositionToleranceMm, sbrtSide.GradIndexForCoord);
			coordBoxLat[1] = GetCoordinates(cooRight, valHURight, sbrtSide.GradientHUPerMm, sbrtSide.DistanceInMm, sbrtSide.PositionToleranceMm, sbrtSide.GradIndexForCoord);
			//coordBoxLat[2] = ((coordBoxRight + coordBoxLeft) / 2);
			if (coordBoxLat[0] != 0 && coordBoxLat[1] != 0 && Math.Abs(coordBoxLat[1] - coordBoxLat[0]) < expectedWidth + widthTolerans && Math.Abs(coordBoxLat[1] - coordBoxLat[0]) > expectedWidth - widthTolerans)
			{
				checkResult = true;
            }




			

			MessageBox.Show("Width of slanted box side " + Math.Abs(coordBoxLat[1] - coordBoxLat[0]).ToString("0.0"));
			return checkResult;
		}




		/// <summary>
		/// Gets the coordinates of the bottom of the SBRT frame, given the plan and the position of interest
		/// Takes the position in center of image in z-coord given by "dicomPosition"
		/// </summary>
		/// <param name="plan"></param>
		/// <param name="dicomPosition"></param>
		/// <returns></returns>
		private int GetSBRTBottomCoord(Image image, VVector dicomPosition)
		{

			double imageSizeX = image.XRes * image.XSize;
			double imageSizeY = image.YRes * image.YSize;
			double xLeftUpperCorner = image.Origin.x - image.XRes / 2;  // Dicomcoord in upper left corner ( NOT middle of voxel in upper left corner)
			double yLeftUpperCorner = image.Origin.y - image.YRes / 2;  // Dicomcoord in upper left corner ( NOT middle of voxel in upper left corner)

			VVector bottomProfileStart = dicomPosition;                      // only to get the z-coord of the user origo, x and y coord will be reassigned
			bottomProfileStart.x = xLeftUpperCorner + imageSizeX / 2;           // center of the image in x-direction
			bottomProfileStart.y = yLeftUpperCorner + imageSizeY - image.YRes;  // start 1 pixel in from bottom...
			double steps = image.YRes;                        //  (mm/voxel) to make the steps 1 pixel wide, can skip this if 1 mm steps is wanted

			VVector bottomProfileEnd = bottomProfileStart;
			bottomProfileEnd.y -= 200 * steps;                                  // endpoint 200 steps in -y direction, i.e. 20 cm if 1 mm pixels

			var samplesY = (int)Math.Ceiling((bottomProfileStart - bottomProfileEnd).Length / steps);


			//***********  Gradient patter describing expected profile in HU of the sbrt-box bottom **********

			PatternGradient sbrt = new PatternGradient();
			sbrt.DistanceInMm = new List<double>() { 0, 4.4, 12.3, 2 };        // distance between gradients, mean values from profiling 10 pat
			sbrt.GradientHUPerMm = new List<int>() { 80, -80, 80, -80 };      // ,  inner shell can be separated from the box, larger tolerance
			sbrt.PositionToleranceMm = new List<int>() { 0, 2, 1, 3 };        // tolerance for the gradient position, needs to be tight as some ct couch tops have almost the same dimensions
			sbrt.GradIndexForCoord = 2;                                     // index of gradient position to return (zero based index)
			double coordBoxBottom = 0;
			int tries = 0;
			List<double> valHU = new List<double>();
			List<double> coo = new List<double>();

			// Per default, try to find bottom in center, in approximately 1 of 20 cases this failes due to couch top structures or image quality, try each side of center
			while (coordBoxBottom == 0 && tries < 3)
			{

				var profY = image.GetImageProfile(bottomProfileStart, bottomProfileEnd, new double[samplesY]);
				// Imageprofile gets a VVector back, take the coordinates and respective HU and put them in two Lists of double, might be better ways of doing this...
				tries++;
				for (int i = 0; i < samplesY; i++)
				{
					valHU.Add(profY[i].Value);
					coo.Add(profY[i].Position.y);
				}

				// Get the coordinate (dicom) that represents inner bottom of SBRT frame 
				coordBoxBottom = GetCoordinates(coo, valHU, sbrt.GradientHUPerMm, sbrt.DistanceInMm, sbrt.PositionToleranceMm, sbrt.GradIndexForCoord);
				// in the SBRT frame; VRT 0, which we are looking for, is approximately 1 mm above this gradient position, add 1 mm before returning
				if (coordBoxBottom != 0)
				{
					coordBoxBottom -= 1;        // TODO: this works for HFS and FFS, HFP and FFP should be handled
					break;
				}
				else  // if bottom not found at center of image try first 100 mm left, then right
				{
					valHU.Clear();
					coo.Clear();
					if (tries == 1)
					{
						bottomProfileStart.x -= 100;
						bottomProfileEnd.x -= 100;
					}
					else
					{
						bottomProfileStart.x += 200;
						bottomProfileEnd.x += 200;
					}
				}
			}
			return (int)Math.Round(coordBoxBottom);
		}


		private double[] GetSBRTLatCoord(Image image, VVector dicomCoord, int coordSRSBottom)
		{

			// ************************************ get profiles in x direction, left and right side and determine center of box ********************


			double xLeftUpperCorner = image.Origin.x - image.XRes / 2;  // Dicomcoord in upper left corner ( NOT middle of voxel in upper left corner) check for FFS, FFP, HFP
			VVector leftProfileStart = dicomCoord;                // only to get the z-coord of the passed in VVector, x and y coord will be reassigned
			VVector rightProfileStart = dicomCoord;               // only to get the z-coord of the passed in VVector, x and y coord will be reassigned
			leftProfileStart.x = xLeftUpperCorner + image.XRes;         // start 1 pixel in left side
			rightProfileStart.x = xLeftUpperCorner + image.XSize * image.XRes - image.XRes;         // start 1 pixel in right side
			leftProfileStart.y = coordSRSBottom - 91.5;                 // hopefully between fidusles...     
			rightProfileStart.y = leftProfileStart.y;
			double stepsX = image.XRes;             //   (mm/voxel) to make the steps 1 pixel wide, can skip this if 1 mm steps is wanted

			VVector leftProfileEnd = leftProfileStart;
			VVector rightProfileEnd = rightProfileStart;
			leftProfileEnd.x += 100 * stepsX;                   // endpoint 100 steps in  direction
			rightProfileEnd.x -= 100 * stepsX;

			var samplesX = (int)Math.Ceiling((leftProfileStart - leftProfileEnd).Length / stepsX);

			var profLeft = image.GetImageProfile(leftProfileStart, leftProfileEnd, new double[samplesX]);
			var profRight = image.GetImageProfile(rightProfileStart, rightProfileEnd, new double[samplesX]);

			List<double> valHULeft = new List<double>();
			List<double> cooLeft = new List<double>();
			string debugLeft = "";
			for (int i = 0; i < samplesX; i++)
			{
				valHULeft.Add(profLeft[i].Value);
				cooLeft.Add(profLeft[i].Position.x);
				if (i > 0)
				{
					debugLeft += cooLeft[i].ToString("0.0") + "\t" + (valHULeft[i] - valHULeft[i - 1]).ToString("0.0") + "\n";
				}
			}


			List<double> valHURight = new List<double>();
			List<double> cooRight = new List<double>();

			for (int i = 0; i < samplesX; i++)
			{
				valHURight.Add(profRight[i].Value);
				cooRight.Add(profRight[i].Position.x);
			}





			//***********  Gradient patter describing expected profile in HU of the Lax-box side, from outside to inside **********

			PatternGradient sbrtSide = new PatternGradient();
			sbrtSide.DistanceInMm = new List<double>() { 0, 2, 13 };     // distance between gradients, mean values from profiling 10 pat 
			sbrtSide.GradientHUPerMm = new List<int>() { 100, -100, 100 };
			sbrtSide.PositionToleranceMm = new List<int>() { 0, 2, 2 };                        // tolerance for the gradient position
			sbrtSide.GradIndexForCoord = 2;                      // index of gradient position to return (zero based index), i.e. the start of the inner wall

			double[] coordBoxLat = new double[2];
			coordBoxLat[0] = GetCoordinates(cooLeft, valHULeft, sbrtSide.GradientHUPerMm, sbrtSide.DistanceInMm, sbrtSide.PositionToleranceMm, sbrtSide.GradIndexForCoord);
			coordBoxLat[1] = GetCoordinates(cooRight, valHURight, sbrtSide.GradientHUPerMm, sbrtSide.DistanceInMm, sbrtSide.PositionToleranceMm, sbrtSide.GradIndexForCoord);


			return coordBoxLat;
		}

		private double GetSRSLongCoord(Image image, VVector dicomPosition, int coordSRSBottom, double coordBoxLeft, double coordBoxRight)
		{

			
			string debug = "";

			// Start with lower profiles, i.e. to count the number of fidusles determining the long position in decimeter
			// Assuming the bottom part of the wall doesn't flex and there are no roll

			double offsetSides = 2;
			double offsetBottom = 91.5;
			int searchRange = 8;


			VVector leftFidusLowerStart = dicomPosition;                // only to get the z-coord of the dicomPosition, x and y coord will be reassigned
			VVector rightFidusLowerStart = dicomPosition;               // only to get the z-coord of the dicomPosition, x and y coord will be reassigned
			leftFidusLowerStart.x = coordBoxLeft + offsetSides;                        // start a small distance in from gradient found in previous step
			rightFidusLowerStart.x = coordBoxRight - offsetSides;
			leftFidusLowerStart.y = coordSRSBottom - offsetBottom;                  // hopefully between fidusles...
			rightFidusLowerStart.y = leftFidusLowerStart.y;
			double stepLength = 0.5;                                                //   probably need sub-mm steps to get the fidusle-positions

			VVector leftFidusLowerEnd = leftFidusLowerStart;
			VVector rightFidusLowerEnd = rightFidusLowerStart;

			int lowerProfileDistance = 40;                                  // profile length to include all possible fidusles

			leftFidusLowerEnd.y += lowerProfileDistance;                   // distance containing all fidusles determining the Long in 10 cm steps
			rightFidusLowerEnd.y += lowerProfileDistance;                  // distance containing all fidusles determining the Long in 10 cm steps

			var samplesFidusLower = (int)Math.Ceiling((leftFidusLowerStart - leftFidusLowerEnd).Length / stepLength);

			leftFidusLowerStart.x = GetMaxHUX(image, leftFidusLowerStart, leftFidusLowerEnd, searchRange, samplesFidusLower);
			rightFidusLowerStart.x = GetMaxHUX(image, rightFidusLowerStart, rightFidusLowerEnd, -searchRange, samplesFidusLower);
			leftFidusLowerEnd.x = leftFidusLowerStart.x;
			rightFidusLowerEnd.x = rightFidusLowerStart.x;

			int numberOfFidusLeft = GetNumberOfFidus(image, leftFidusLowerStart, leftFidusLowerEnd, lowerProfileDistance * 2);
			int numberOfFidusRight = GetNumberOfFidus(image, rightFidusLowerStart, rightFidusLowerEnd, lowerProfileDistance * 2);



			// Since the SRS-box walls flexes, the x-coordinate for upper profile may differ from start to end
			// get the max HU in the upper part of the box ( top-most fidusel ) to determine the final x-value for the profile
			// also need to get the x-value for start of the profile, concentrate on index-fidusle (Vrt 95 in SBRT frame coordinates)

			// Start with finding the optimal position in x for the index fidusle, left and right

			VVector leftIndexFidusStart = dicomPosition;
			VVector rightIndexFidusStart = dicomPosition;
			leftIndexFidusStart.x = coordBoxLeft + offsetSides;
			rightIndexFidusStart.x = coordBoxRight - offsetSides;
			leftIndexFidusStart.y = coordSRSBottom - offsetBottom;
			rightIndexFidusStart.y = coordSRSBottom - offsetBottom;
			VVector leftIndexFidusEnd = leftIndexFidusStart;
			VVector rightIndexFidusEnd = rightIndexFidusStart;
			int shortProfileLength = 20;
			leftIndexFidusEnd.y -= shortProfileLength;
			rightIndexFidusEnd.y -= shortProfileLength;

			VVector leftFidusUpperStart = leftIndexFidusStart;      // to get the z-coord, x and y coord will be reassigned
			VVector rightFidusUpperStart = rightIndexFidusStart;

			leftFidusUpperStart.x = GetMaxHUX(image, leftIndexFidusStart, leftIndexFidusEnd, searchRange, shortProfileLength * 2);
			rightFidusUpperStart.x = GetMaxHUX(image, rightIndexFidusStart, rightIndexFidusEnd, -searchRange, shortProfileLength * 2);


			// startposition for profiles determined, next job the endposition

			int upperProfileDistance = 115;
			VVector leftTopFidusStart = dicomPosition;
			VVector rightTopFidusStart = dicomPosition;
			leftTopFidusStart.x = coordBoxLeft + offsetSides;
			rightTopFidusStart.x = coordBoxRight - offsetSides;
			leftTopFidusStart.y = coordSRSBottom - offsetBottom - upperProfileDistance + shortProfileLength;  // unnecessary complex but want to get the profile in same direction
			rightTopFidusStart.y = coordSRSBottom - offsetBottom - upperProfileDistance + shortProfileLength;
			VVector leftTopFidusEnd = leftTopFidusStart;
			VVector rightTopFidusEnd = rightTopFidusStart;

			leftTopFidusEnd.y -= shortProfileLength;
			rightTopFidusEnd.y -= shortProfileLength;

			VVector leftFidusUpperEnd = leftTopFidusEnd;
			VVector rightFidusUpperEnd = rightTopFidusEnd;

			leftFidusUpperEnd.x = GetMaxHUX(image, leftTopFidusStart, leftTopFidusEnd, searchRange, shortProfileLength * 2);                      // what is the 3 ?????????????????????????????????????
			rightFidusUpperEnd.x = GetMaxHUX(image, rightTopFidusStart, rightTopFidusEnd, -searchRange, shortProfileLength * 2);

			debug += "LeftBox: \t\t" + coordBoxLeft.ToString("0.0") + "\n";
			debug += "LeftLow xstart: \t" + leftFidusLowerStart.x.ToString("0.0") + "\n";

			debug += "Left x start: \t" + leftFidusUpperStart.x.ToString("0.0") + "\n End: \t\t" + leftFidusUpperEnd.x.ToString("0.0") + "\n\n";


			debug += "RightBox: \t\t" + coordBoxRight.ToString("0.0") + "\n";
			debug += "RightLow xstart: \t" + rightFidusLowerStart.x.ToString("0.0") + "\n";
			debug += "Right x start: \t" + rightFidusUpperStart.x.ToString("0.0") + "\n End: \t\t" + rightFidusUpperEnd.x.ToString("0.0") + "\n\n\n";

			double fidusLongLeft = GetLongFidus(image, leftFidusUpperStart, leftFidusUpperEnd, upperProfileDistance * 2);
			double fidusLongRight = GetLongFidus(image, rightFidusUpperStart, rightFidusUpperEnd, upperProfileDistance * 2);

			debug += "Left side long:  " + fidusLongLeft.ToString("0.0") + "\t Right side long:  " + fidusLongRight.ToString("0.0") + "\n\n";
			debug += "Left side fidus:  " + numberOfFidusLeft + "\t Right side fidus:  " + numberOfFidusRight;

			//MessageBox.Show(debug);
			// Also need to check the long coordinate above and below (in z-dir) in case its a boundary case where the number of fidusles 
			// steps up. Only neccesary in case of large value for fidusLong or if a discrepancy between the number of fidusles found left and right,
			// or if the long value is not found. +/- 10 mm shift in z-dir is enough to avoid boundary condition *************TODO: check for FFS!!!!!!!!!!!!!

			double coordSRSLong = 0;

			if (numberOfFidusLeft != numberOfFidusRight || fidusLongLeft > 97 || fidusLongRight > 97 || fidusLongLeft == 0.0 || fidusLongRight == 0)
			{
				int shiftZ = 10;
				leftFidusLowerStart.z += shiftZ;
				leftFidusLowerEnd.z += shiftZ;
				leftFidusUpperEnd.z += shiftZ;
				rightFidusLowerStart.z += shiftZ;
				rightFidusLowerEnd.z += shiftZ;
				rightFidusUpperEnd.z += shiftZ;


				int nOfFidusLeft1 = GetNumberOfFidus(image, leftFidusLowerStart, leftFidusLowerEnd, lowerProfileDistance * 2);
				double fidusLLeft1 = GetLongFidus(image, leftFidusLowerStart, leftFidusUpperEnd, upperProfileDistance * 2);
				int nOfFidusRight1 = GetNumberOfFidus(image, rightFidusLowerStart, rightFidusLowerEnd, lowerProfileDistance * 2);
				double fidusLRight1 = GetLongFidus(image, rightFidusLowerStart, rightFidusUpperEnd, upperProfileDistance * 2);


				leftFidusLowerStart.z -= 2 * shiftZ;
				leftFidusLowerEnd.z -= 2 * shiftZ;
				leftFidusUpperEnd.z -= 2 * shiftZ;
				rightFidusLowerStart.z -= 2 * shiftZ;
				rightFidusLowerEnd.z -= 2 * shiftZ;
				rightFidusUpperEnd.z -= 2 * shiftZ;


				int nOfFidusLeft2 = GetNumberOfFidus(image, leftFidusLowerStart, leftFidusLowerEnd, lowerProfileDistance * 2);
				double fidusLLeft2 = GetLongFidus(image, leftFidusLowerStart, leftFidusUpperEnd, upperProfileDistance * 2);
				int nOfFidusRight2 = GetNumberOfFidus(image, rightFidusLowerStart, rightFidusLowerEnd, lowerProfileDistance * 2);
				double fidusLRight2 = GetLongFidus(image, rightFidusLowerStart, rightFidusUpperEnd, upperProfileDistance * 2);


				double coordLong1 = (nOfFidusLeft1 + nOfFidusRight1) * 50 + (fidusLLeft1 + fidusLRight1) / 2;
				double coordLong2 = (nOfFidusLeft2 + nOfFidusRight2) * 50 + (fidusLLeft2 + fidusLRight2) / 2;

				//Check if resonable agreement before assigning the final long coordinate as mean value, hard coded values for uncertainty...
				// left and right side should be within 2 mm and not zero
				// moved 10 mm in both directions i.e. expected difference in long is 20 mm
				if (nOfFidusLeft1 == nOfFidusRight1 && nOfFidusLeft2 == nOfFidusRight2 && Math.Abs(fidusLLeft1 - fidusLRight1) < 2 && Math.Abs(fidusLLeft2 - fidusLRight2) < 2 && fidusLLeft1 != 0 && fidusLLeft2 != 0)
				{
					if (Math.Abs(coordLong2 - coordLong1) > 18 && Math.Abs(coordLong2 - coordLong1) < 22)
					{
						coordSRSLong = (coordLong1 + coordLong2) / 2;
					}
				}
				else
				{
					//	MessageBox.Show("Problem :first " + coordLong1.ToString("0.0") + "\t second " + coordLong2.ToString("0.0"));
				}
			}
			else
			{
				coordSRSLong = (numberOfFidusLeft + numberOfFidusRight) * 50 + (fidusLongLeft + fidusLongRight) / 2;
				//MessageBox.Show("Left side " + fidusLongLeft.ToString("0.0") + "\t Right side " + fidusLongRight.ToString("0.0"));
			}
			return coordSRSLong;
		}




		/// <summary>
		/// gets the x-value where maximum HU is found when stepping the y-profile in the direction (Dicom) and range given in steps of 0.1 mm
		/// </summary>
		/// <param name="image"></param>
		/// <param name="fidusStart"></param>
		/// <param name="fidusEnd"></param>
		/// <param name="dirLengthInmm"></param>
		/// <param name="samples"></param>
		/// <returns></returns>
		public static double GetMaxHUX(Image image, VVector fidusStart, VVector fidusEnd, double dirLengthInmm, int samples)
		{
			double newMax = 0.0;
			string debugM = "";
			List<double> HUTemp = new List<double>();
			List<double> cooTemp = new List<double>();
			double finalXValue = 0;
			for (int s = 0; s < 10 * Math.Abs(dirLengthInmm); s++)
			{
				fidusStart.x += 0.1 * dirLengthInmm / Math.Abs(dirLengthInmm);  // ugly way to get the direction                     
				fidusEnd.x = fidusStart.x;


				var profFidus = image.GetImageProfile(fidusStart, fidusEnd, new double[samples]);

				for (int i = 0; i < samples; i++)
				{
					HUTemp.Add(profFidus[i].Value);
					cooTemp.Add(profFidus[i].Position.y);
				}
				if (HUTemp.Max() > newMax)
				{
					newMax = HUTemp.Max();
					finalXValue = fidusStart.x;
					debugM += finalXValue.ToString("0.0") + "\t" + newMax.ToString("0.0") + "\n";
				}
				HUTemp.Clear();
				cooTemp.Clear();
			}
			//MessageBox.Show(debugM);
			return finalXValue;
		}



		public int GetNumberOfFidus(Image image, VVector fidusStart, VVector fidusEnd, int samples)
		{
			List<double> valHU = new List<double>();
			List<double> coord = new List<double>();
			double findGradientResult;

			var profFidus = image.GetImageProfile(fidusStart, fidusEnd, new double[samples]);

			for (int i = 0; i < samples; i++)
			{
				valHU.Add(profFidus[i].Value);
				coord.Add(profFidus[i].Position.y);
			}
			var fid = new PatternGradient();
			fid.DistanceInMm = new List<double>() { 0, 2 };         // distance between gradients
			fid.GradientHUPerMm = new List<int>() { 100, -100 };    // smallest number of fidusles is one?  actually its zero! TODO have to handle this case!!
			fid.PositionToleranceMm = new List<int>() { 0, 2 };     // tolerance for the gradient position, parameter to optimize depending probably of resolution of profile
			fid.GradIndexForCoord = 0;                              // index of gradient position to return, in this case used only as a counter for number of fidusles

			findGradientResult = GetCoordinates(coord, valHU, fid.GradientHUPerMm, fid.DistanceInMm, fid.PositionToleranceMm, fid.GradIndexForCoord);
			// keep adding gradient pattern until no more fidusles found
			while (findGradientResult != 0.0)
			{
				fid.DistanceInMm.Add(3);
				fid.GradientHUPerMm.Add(100);
				fid.PositionToleranceMm.Add(2);
				fid.DistanceInMm.Add(2);
				fid.GradientHUPerMm.Add(-100);
				fid.PositionToleranceMm.Add(2);
				fid.GradIndexForCoord++;
				findGradientResult = GetCoordinates(coord, valHU, fid.GradientHUPerMm, fid.DistanceInMm, fid.PositionToleranceMm, fid.GradIndexForCoord);
			}
			return fid.GradIndexForCoord;
		}


		public double GetLongFidus(Image image, VVector fidusStart, VVector fidusEnd, int samples)
		{
			List<double> valHU = new List<double>();
			List<double> coord = new List<double>();
			double findFirstFidus;
			double findSecondFidus;


			var profFidus = image.GetImageProfile(fidusStart, fidusEnd, new double[samples]);

			for (int i = 0; i < samples; i++)
			{
				valHU.Add(profFidus[i].Value);
				coord.Add(profFidus[i].Position.y);
			}


			int diagFidusGradient = 100 / (int)Math.Round(Math.Sqrt((Math.Sqrt(image.ZRes))));  //diagonal fidusle have flacker gradient
																								//double diagFidusGradientMinLength = 2 * Math.Sqrt(image.ZRes);
			double diagFidusWidth = 1 + 0.5 * Math.Sqrt(image.ZRes);                        // and is wider, both values depend on resolution in Z


			var fid = new PatternGradient();
			fid.DistanceInMm = new List<double>() { 0, 2, 49, diagFidusWidth, 99, 2 };//};        // distance between gradients
			fid.GradientHUPerMm = new List<int>() { 100, -100, diagFidusGradient, -diagFidusGradient, 100, -100 };//};    // diagonal fidusle have flacker gradient
																												  //fid.MinGradientLength = new List<double>() { 0, 0, 0, 0, 0, 0 };//};        // minimum length of gradient, needed in case of noicy image
			fid.PositionToleranceMm = new List<int>() { 2, 3, 105, 4, 105, 3 };                        // tolerance for the gradient position, in this case the maximum distance is approx 105 mm
			fid.GradIndexForCoord = 0;                      // index of gradient position to return (zero based index)

			// Finding position of the gradient start is not enough since the long fidusle is diagonal and also changes width depending of the resolution of the image in z-dir, 
			// have to take the mean position before and after.
			double findFirstFidusStart = GetCoordinates(coord, valHU, fid.GradientHUPerMm, fid.DistanceInMm, fid.PositionToleranceMm, fid.GradIndexForCoord);
			fid.GradIndexForCoord = 1;
			double findFirstFidusEnd = GetCoordinates(coord, valHU, fid.GradientHUPerMm, fid.DistanceInMm, fid.PositionToleranceMm, fid.GradIndexForCoord);
			findFirstFidus = (findFirstFidusStart + findFirstFidusEnd) / 2;
			//Find position of second fidus (diagonal)

			fid.GradIndexForCoord = 2;
			double findSecondFidusStart = GetCoordinates(coord, valHU, fid.GradientHUPerMm, fid.DistanceInMm, fid.PositionToleranceMm, fid.GradIndexForCoord);
			fid.GradIndexForCoord = 3;
			double findSecondFidusEnd = GetCoordinates(coord, valHU, fid.GradientHUPerMm, fid.DistanceInMm, fid.PositionToleranceMm, fid.GradIndexForCoord);
			findSecondFidus = (findSecondFidusStart + findSecondFidusEnd) / 2;



			return Math.Abs(findSecondFidus - findFirstFidus);



		}



		/// <summary>
		/// getCoordinates gives the Dicom-coordinates of a gradient 
		/// </summary>
		/// <param name="coord"> 1D coordinates of a profile</param>
		/// <param name="valueHU"> HU-valus of the profile</param>
		/// <param name="hUPerMm"> Gradient to search for in HU/mm with sign indicating direction</param>
		/// <param name="distMm"> Distance in mm to the next gradient</param>
		/// <param name="posTolMm"> Tolerance of position of found gradient in mm</param>
		/// <returns></returns>
		public double GetCoordinates(List<double> coord, List<double> valueHU, List<int> hUPerMm, List<double> distMm, List<int> posTolMm, int indexToReturn)
		{
			string debug = "";
			double[] grad = new double[coord.Count - 1];
			double[] pos = new double[coord.Count - 1];
			int index = 0;

			double gradientStart;
			double gradientEnd;
			double gradientMiddle;
			// resample profile to gradient with position inbetween profile points ( number of samples decreases with one)
			for (int i = 0; i < coord.Count - 2; i++)
			{
				pos[i] = (coord[i] + coord[i + 1]) / 2;
				grad[i] = (valueHU[i + 1] - valueHU[i]) / Math.Abs(coord[i + 1] - coord[i]);
			}



			List<double> gradPosition = new List<double>();
			int indexToReturnToInCaseOfFail = 0;

			for (int i = 0; i < pos.Count(); i++)
			{
				if (index == hUPerMm.Count())                        //break if last condition passed 
				{
					break;
				}
				// if gradient larger than given gradient and in the same direction
				//if (Math.Abs((valueHU[i + 1] - valueHU[i]) / Math.Abs(coord[i + 1] - coord[i])) > (Math.Abs(hUPerMm[index])) && SameSign(grad[i], hUPerMm[index]))
				if (Math.Abs(grad[i]) > Math.Abs(hUPerMm[index]) && SameSign(grad[i], hUPerMm[index]))
				{
					gradientStart = pos[i];
					gradientEnd = pos[i];

					//Keep stepping up while gradient larger than given huPerMm
					while (Math.Abs(grad[i]) > (Math.Abs(hUPerMm[index])) && SameSign(grad[i], hUPerMm[index]) && i < coord.Count - 2)
					{
						i++;
						gradientEnd = pos[i];
						if (index == 0)
						{
							indexToReturnToInCaseOfFail = i + 1; // if the search fails, i.e. can not find next gradient within the distance given, return to position directly after first gradient ends
						}
					}
					gradientMiddle = (gradientStart + gradientEnd) / 2;
					// if this is the first gradient (i.e. index == 0), cannot yet compare the distance between the gradients, step up index and continue
					if (index == 0)
					{
						gradPosition.Add(gradientMiddle);
						index++;
					}
					// if gradient found before expected position (outside tolerance), keep looking
					else if (Math.Abs(gradientMiddle - gradPosition[index - 1]) < distMm[index] - posTolMm[index] && i < pos.Count() - 2)
					{
						i++;
						//MessageBox.Show(Math.Abs(gradientMiddle - gradPosition[index - 1]).ToString("0.0"));
					}
					// if next gradient not found within tolerance distance, means that the first gradient is probably wrong, reset index
					else if ((Math.Abs(gradientMiddle - gradPosition[index - 1]) > (Math.Abs(distMm[index]) + posTolMm[index])))
					{
						debug += "Fail " + (Math.Abs(gradientMiddle - gradPosition[index - 1])).ToString("0.0") + "\t" + (distMm[index] + posTolMm[index]).ToString("0.0") + "\n";
						gradPosition.Clear();
						index = 0;
						i = indexToReturnToInCaseOfFail;
					}
					//  compare the distance between the gradients to the criteria given, step up index and continue if within tolerance
					else if ((Math.Abs(gradientMiddle - gradPosition[index - 1]) > (distMm[index] - posTolMm[index])) && (Math.Abs(gradientMiddle - gradPosition[index - 1]) < (distMm[index] + posTolMm[index])))
					{
						//debug += pos[i].ToString("0.0") + "\t" + (gradPosition[index] - gradPosition[index - 1]).ToString("0.0") + "\t" + grad[i].ToString("0.0") + "\n";
						gradPosition.Add(gradientMiddle);
						index++;
						if (index == 1)
						{
							indexToReturnToInCaseOfFail = i;
						}
					}
					else
					{   // if not the first gradient and the distance betwen the gradients are not met within the tolerance; reset index and positions and continue search
						// reset search from second gradient position to avoid missing the actual gradient.
						if (gradPosition.Count > 1 && indexToReturnToInCaseOfFail > 0)
						{
							i = indexToReturnToInCaseOfFail;
						}
						gradPosition.Clear();
						index = 0;
					}
				}
			}
			if (index == hUPerMm.Count())
			{
				return gradPosition[indexToReturn];
			}
			else
			{
				return 0.0;
			}
		} // end method 


		//overloaded to also get minimum expected gradient length, needed for noisy images 
		public double GetCoordinates(List<double> coord, List<double> valueHU, List<int> hUPerMm, List<double> distMm, List<int> posTolMm, int indexToReturn, List<double> minGradientLength)
		{
			string debug = "";
			double[] grad = new double[coord.Count - 1];
			double[] pos = new double[coord.Count - 1];
			int index = 0;

			double gradientStart;
			double gradientEnd;
			double gradientMiddle;
			// resample profile to gradient with position inbetween profile points ( number of samples decreases with one)
			for (int i = 0; i < coord.Count - 2; i++)
			{
				pos[i] = (coord[i] + coord[i + 1]) / 2;
				grad[i] = (valueHU[i + 1] - valueHU[i]) / Math.Abs(coord[i + 1] - coord[i]);
			}



			List<double> gradPosition = new List<double>();
			int indexToReturnToInCaseOfFail = 0;

			for (int i = 0; i < pos.Count(); i++)
			{
				if (index == hUPerMm.Count())                        //break if last condition passed 
				{
					break;
				}
				// if gradient larger than given gradient and in the same direction
				//if (Math.Abs((valueHU[i + 1] - valueHU[i]) / Math.Abs(coord[i + 1] - coord[i])) > (Math.Abs(hUPerMm[index])) && SameSign(grad[i], hUPerMm[index]))
				if (Math.Abs(grad[i]) > Math.Abs(hUPerMm[index]) && SameSign(grad[i], hUPerMm[index]))
				{
					gradientStart = pos[i];
					gradientEnd = pos[i];
					i++;
					//Keep stepping up while gradient larger than given huPerMm
					while (Math.Abs(grad[i]) > (Math.Abs(hUPerMm[index])) && SameSign(grad[i], hUPerMm[index]) && i < coord.Count - 2)
					{
						gradientEnd = pos[i];
						if (index == 0)
						{
							indexToReturnToInCaseOfFail = i + 1; // if the search fails, i.e. can not find next gradient within the distance given, return to position directly after first gradient ends
						}
						i++;
					}
					gradientMiddle = (gradientStart + gradientEnd) / 2;
					if (Math.Abs(gradientStart - gradientEnd) >= minGradientLength[index])

					{

						// if this is the first gradient (i.e. index == 0), cannot yet compare the distance between the gradients, step up index and continue
						if (index == 0)
						{
							gradPosition.Add(gradientMiddle);
							index++;
						}
						// if gradient found before expected position (outside tolerance), keep looking
						else if (Math.Abs(gradientMiddle - gradPosition[index - 1]) < distMm[index] - posTolMm[index] && i < pos.Count() - 2)
						{
							i++;
							//MessageBox.Show(Math.Abs(gradientMiddle - gradPosition[index - 1]).ToString("0.0"));
						}
						// if next gradient not found within tolerance distance, means that the first gradient is probably wrong, reset index
						else if ((Math.Abs(gradientMiddle - gradPosition[index - 1]) > (Math.Abs(distMm[index]) + posTolMm[index])))
						{
							debug += "Fail " + (Math.Abs(gradientMiddle - gradPosition[index - 1])).ToString("0.0") + "\t" + (distMm[index] + posTolMm[index]).ToString("0.0") + "\n";
							gradPosition.Clear();
							index = 0;
							i = indexToReturnToInCaseOfFail;
						}
						//  compare the distance between the gradients to the criteria given, step up index and continue if within tolerance
						else if ((Math.Abs(gradientMiddle - gradPosition[index - 1]) > (distMm[index] - posTolMm[index])) && (Math.Abs(gradientMiddle - gradPosition[index - 1]) < (distMm[index] + posTolMm[index])))
						{
							//debug += pos[i].ToString("0.0") + "\t" + (gradPosition[index] - gradPosition[index - 1]).ToString("0.0") + "\t" + grad[i].ToString("0.0") + "\n";
							gradPosition.Add(gradientMiddle);
							index++;
							if (index == 1)
							{
								indexToReturnToInCaseOfFail = i;
							}
						}
						else
						{   // if not the first gradient and the distance betwen the gradients are not met within the tolerance; reset index and positions and continue search
							// reset search from second gradient position to avoid missing the actual gradient.
							if (gradPosition.Count > 1 && indexToReturnToInCaseOfFail > 0)
							{
								i = indexToReturnToInCaseOfFail;
							}
							gradPosition.Clear();
							index = 0;
						}

					}
				}
			}
			if (index == hUPerMm.Count())
			{
				return gradPosition[indexToReturn];
			}
			else
			{
				return 0.0;
			}
		} // end method GetCoordinates
	}
}
