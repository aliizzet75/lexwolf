#!/usr/bin/env python3
"""
Simple test for quality verification API endpoints
"""

import sys
import os
sys.path.insert(0, 'backend')

def test_quality_api_simple():
    """Test quality API functionality without full server"""
    print("Testing quality API functionality...")
    
    try:
        # Import the quality service directly
        from services.quality_verification import QualityVerificationService
        
        # Create service instance
        quality_service = QualityVerificationService()
        
        # Test content verification
        test_content = """
        Sehr geehrte Damen und Herren,

        Gemäß § 1 BGB ist ein Vertrag zustande gekommen, wenn ein 
        Antrag auf Abschluss eines Vertrages durch einen anderen 
        angenommen wurde.

        Mit freundlichen Grüßen
        """
        
        result = quality_service.verify_generated_content(test_content)
        
        print("Quality Verification Result:")
        print(f"  Passes Quality: {result.get('passes_quality', False)}")
        print(f"  Quality Score: {result.get('quality_score', 0.0):.2f}")
        print(f"  Source Accuracy: {result.get('source_verification', {}).get('accuracy_rate', 0.0):.2f}")
        print(f"  Hallucinations Found: {result.get('hallucination_check', {}).get('hallucinations_found', False)}")
        
        # Test metrics
        metrics = quality_service.get_metrics()
        print(f"  Total Checks: {metrics.get('total_checks', 0)}")
        print(f"  Passed Checks: {metrics.get('passed_checks', 0)}")
        
        return True
        
    except Exception as e:
        print(f"Error testing quality API: {e}")
        return False

if __name__ == "__main__":
    success = test_quality_api_simple()
    if success:
        print("\n✅ Quality API test completed successfully!")
    else:
        print("\n❌ Quality API test failed!")